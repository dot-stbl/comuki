using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// The webhook pipeline truth-table: every branch of
/// <see cref="WebhookIntakeService.HandleAsync"/> is exercised against a
/// mock <see cref="IIntakeStore"/> and a mock <see cref="ITicketSourceProvider"/>
/// so the pipeline logic is asserted in isolation from the EF store and
/// from the per-provider signature/normalize logic.
/// </summary>
public sealed class WebhookIntakeServiceShould
{
    private readonly DateTimeOffset now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly IIntakeStore store = Substitute.For<IIntakeStore>();
    private readonly IRunLauncher runLauncher = Substitute.For<IRunLauncher>();
    private readonly ITicketSourceProvider provider = Substitute.For<ITicketSourceProvider>();
    private readonly ITicketSyncPort syncPort = Substitute.For<ITicketSyncPort>();

    public WebhookIntakeServiceShould()
    {
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        provider.DeliveryIdOf(Arg.Any<WebhookDelivery>()).Returns("delivery-1");
    }

    private WebhookIntakeService BuildService()
    {
        var registry = new TicketProviderRegistry([provider], [syncPort]);
        return new WebhookIntakeService(
            store,
            registry,
            runLauncher,
            TimeProvider.System,
            NullLogger<WebhookIntakeService>.Instance);
    }

    private static WebhookDelivery NewDelivery()
    {
        return new WebhookDelivery(
            Body: new ReadOnlyMemory<byte>([0x01, 0x02]),
            Headers: new Dictionary<string, string>(),
            Query: new Dictionary<string, string>());
    }

    private SourceConnection BuildConnection()
    {
        return SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "Main",
            "{}",
            "HOOK_SECRET",
            "abcdefghijklmnop",
            now);
    }

    [Fact(DisplayName = "Given the native source key, when HandleAsync runs, then 404 with source_provider_not_found is returned and nothing is inserted")]
    public async Task NativeSourceKeyReturnsNotFoundAsync()
    {
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.Native,
            "abcdefghijklmnop",
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(404);
        receipt.Code.ShouldBe("intake.source_provider_not_found");
        await store.DidNotReceive().FindConnectionByWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unregistered source key, when HandleAsync runs, then 404 is returned and no store call is made")]
    public async Task UnknownProviderReturnsNotFoundAsync()
    {
        var service = BuildService();

        var receipt = await service.HandleAsync(
            "ghost-tracker",
            "abcdefghijklmnop",
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(404);
        receipt.Code.ShouldBe("intake.source_provider_not_found");
        await store.DidNotReceive().FindConnectionByWebhookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a registered provider but no connection on the webhook key, when HandleAsync runs, then 404 with connection_not_found is returned")]
    public async Task MissingConnectionReturnsNotFoundAsync()
    {
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, "missing-key", Arg.Any<CancellationToken>())
            .Returns((SourceConnection?)null);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            "missing-key",
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(404);
        receipt.Code.ShouldBe("intake.connection_not_found");
        await store.DidNotReceive().TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the delivery row insert loses the race, when HandleAsync runs, then 200 with outcome=replay is returned")]
    public async Task DuplicateDeliveryReturnsReplayAsync()
    {
        var connection = BuildConnection();
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(false);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Replay);
        await store.DidNotReceive().MarkDeliveryOutcomeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await runLauncher.DidNotReceive().LaunchAsync(Arg.Any<ProjectId>(), Arg.Any<SourceConnection>(), Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the signature verification rejects, when HandleAsync runs, then 401 with signature_invalid is returned and the rejection is recorded")]
    public async Task BadSignatureReturns401AndMarksAsync()
    {
        var connection = BuildConnection();
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(false);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(401);
        receipt.Code.ShouldBe("intake.signature_invalid");
        await store.Received(1).MarkDeliveryOutcomeAsync(
            Arg.Any<Guid>(),
            DeliveryOutcomes.Rejected,
            "signature mismatch",
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the payload normalizes to null, when HandleAsync runs, then 200 with outcome=skipped is returned")]
    public async Task NormalizeReturnsNullMarksSkippedAsync()
    {
        var connection = BuildConnection();
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(true);
        provider.Normalize(Arg.Any<WebhookDelivery>(), connection).Returns((IncomingTicket?)null);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Skipped);
        await store.Received(1).MarkDeliveryOutcomeAsync(
            Arg.Any<Guid>(),
            DeliveryOutcomes.Skipped,
            "not a ticket event",
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no enabled rule matches the ticket, when HandleAsync runs, then 200 with outcome=filtered is returned and the dismissed ticket is persisted")]
    public async Task NoMatchingRuleMarksFilteredAsync()
    {
        var connection = BuildConnection();
        var ticket = IncomingTicket.Create(
            connection.ProjectId,
            TicketProvider.GitHub,
            "acme/app#42",
            "Title",
            "Body",
            "ada",
            "https://example.com/42",
            "acme/app",
            ["bug"],
            InboundTicketKind.Issue,
            now);
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(true);
        provider.Normalize(Arg.Any<WebhookDelivery>(), connection).Returns(ticket);
        store.ListEnabledRulesAsync(connection.ProjectId, Arg.Any<CancellationToken>()).Returns([]);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Filtered);
        await store.Received(1).AddDismissedTicketAsync(
            Arg.Is<IncomingTicket>(static t => t.ExternalId == "acme/app#42"),
            Arg.Any<CancellationToken>());
        await store.Received(1).MarkDeliveryOutcomeAsync(
            Arg.Any<Guid>(),
            DeliveryOutcomes.Filtered,
            "acme/app#42",
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an active ticket already exists for the issue, when HandleAsync runs, then 200 with outcome=duplicate is returned")]
    public async Task ActiveLockConflictReturnsDuplicateAsync()
    {
        var connection = BuildConnection();
        var ticket = IncomingTicket.Create(
            connection.ProjectId,
            TicketProvider.GitHub,
            "acme/app#7",
            "Title",
            "Body",
            "ada",
            "https://example.com/7",
            "acme/app",
            ["bug"],
            InboundTicketKind.Issue,
            now);
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(true);
        provider.Normalize(Arg.Any<WebhookDelivery>(), connection).Returns(ticket);
        var rule = AdmissionRule.Create(connection.ProjectId, AdmissionMode.Inbox, "{}", now);
        store.ListEnabledRulesAsync(connection.ProjectId, Arg.Any<CancellationToken>()).Returns([rule]);
        store.TryInsertTicketAsync(Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>()).Returns((IncomingTicket?)null);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Duplicate);
        await store.DidNotReceive().AddDismissedTicketAsync(Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>());
        await runLauncher.DidNotReceive().LaunchAsync(Arg.Any<ProjectId>(), Arg.Any<SourceConnection>(), Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the matching rule is watch mode, when HandleAsync runs, then a run is launched, the ticket is claimed and outcome=admitted is returned")]
    public async Task WatchModeLaunchesRunAndAdmitsAsync()
    {
        var connection = BuildConnection();
        var ticket = IncomingTicket.Create(
            connection.ProjectId,
            TicketProvider.GitHub,
            "acme/app#9",
            "Title",
            "Body",
            "ada",
            "https://example.com/9",
            "acme/app",
            ["bug"],
            InboundTicketKind.Issue,
            now);
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(true);
        provider.Normalize(Arg.Any<WebhookDelivery>(), connection).Returns(ticket);
        var rule = AdmissionRule.Create(connection.ProjectId, AdmissionMode.Watch, "{}", now);
        store.ListEnabledRulesAsync(connection.ProjectId, Arg.Any<CancellationToken>()).Returns([rule]);
        store.TryInsertTicketAsync(Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>()).Returns(ticket);
        var runId = RunId.New();
        runLauncher.LaunchAsync(connection.ProjectId, connection, ticket, Arg.Any<CancellationToken>()).Returns(runId);
        store.TryMarkClaimedAsync(ticket.Id, runId, Arg.Any<CancellationToken>()).Returns(true);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Admitted);
        receipt.Detail.ShouldBe("acme/app#9");
        await runLauncher.Received(1).LaunchAsync(connection.ProjectId, connection, ticket, Arg.Any<CancellationToken>());
        await store.Received(1).TryMarkClaimedAsync(ticket.Id, runId, Arg.Any<CancellationToken>());
        await store.Received(1).MarkDeliveryOutcomeAsync(
            Arg.Any<Guid>(),
            DeliveryOutcomes.Admitted,
            "acme/app#9",
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the matching rule is inbox mode, when HandleAsync runs, then no run is launched and outcome=pending is returned")]
    public async Task InboxModeParksTicketAsync()
    {
        var connection = BuildConnection();
        var ticket = IncomingTicket.Create(
            connection.ProjectId,
            TicketProvider.GitHub,
            "acme/app#11",
            "Title",
            "Body",
            "ada",
            "https://example.com/11",
            "acme/app",
            ["bug"],
            InboundTicketKind.Issue,
            now);
        store.FindConnectionByWebhookAsync(TicketProviderKeys.GitHub, connection.WebhookKey, Arg.Any<CancellationToken>())
            .Returns(connection);
        store.TryInsertDeliveryAsync(Arg.Any<IntakeDelivery>(), Arg.Any<CancellationToken>()).Returns(true);
        provider.VerifySignature(connection, Arg.Any<WebhookDelivery>()).Returns(true);
        provider.Normalize(Arg.Any<WebhookDelivery>(), connection).Returns(ticket);
        var rule = AdmissionRule.Create(connection.ProjectId, AdmissionMode.Inbox, "{}", now);
        store.ListEnabledRulesAsync(connection.ProjectId, Arg.Any<CancellationToken>()).Returns([rule]);
        store.TryInsertTicketAsync(Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>()).Returns(ticket);
        var service = BuildService();

        var receipt = await service.HandleAsync(
            TicketProviderKeys.GitHub,
            connection.WebhookKey,
            NewDelivery(),
            TestContext.Current.CancellationToken);

        receipt.StatusCode.ShouldBe(200);
        receipt.Outcome.ShouldBe(DeliveryOutcomes.Pending);
        await runLauncher.DidNotReceive().LaunchAsync(Arg.Any<ProjectId>(), Arg.Any<SourceConnection>(), Arg.Any<IncomingTicket>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().TryMarkClaimedAsync(Arg.Any<IncomingTicketId>(), Arg.Any<RunId>(), Arg.Any<CancellationToken>());
        await store.Received(1).MarkDeliveryOutcomeAsync(
            Arg.Any<Guid>(),
            DeliveryOutcomes.Pending,
            "acme/app#11",
            Arg.Any<CancellationToken>());
    }
}
