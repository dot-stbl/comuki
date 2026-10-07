using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Application.Ports.Tickets;
using Comuki.Modules.Integrations.Application.Tickets;
using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Integrations.Unit;

/// <summary>
/// Native ticket create path: validation, generated external id, launch +
/// claim, and conflict when the unique insert loses.
/// </summary>
public sealed class CreateNativeInboundItemHandlerShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
    private readonly IIntegrationsStore store = Substitute.For<IIntegrationsStore>();
    private readonly IRunLauncher runLauncher = Substitute.For<IRunLauncher>();
    private readonly FakeTime clock;

    public CreateNativeInboundItemHandlerShould()
    {
        clock = new FakeTime(now);
    }

    [Fact(DisplayName = "Given a valid command, when Handle runs, then ticket is inserted, launched and claimed")]
    public async Task CreateLaunchesAndClaimsAsync()
    {
        InboundItem? inserted = null;
        store.TryInsertTicketAsync(Arg.Any<InboundItem>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                inserted = callInfo.Arg<InboundItem>();
                return inserted;
            });
        var runId = RunId.New();
        runLauncher.LaunchAsync(Arg.Any<ProjectId>(), Arg.Any<Domain.Connections.SourceConnection>(), Arg.Any<InboundItem>(), Arg.Any<CancellationToken>())
            .Returns(runId);
        store.TryMarkClaimedAsync(Arg.Any<InboundItemId>(), runId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateNativeInboundItemHandler(
            store,
            runLauncher,
            clock,
            new CreateNativeInboundItemValidator(),
            NullLogger<CreateNativeInboundItemHandler>.Instance);

        var projectId = ProjectId.New();
        var view = await handler.HandleAsync(
            new CreateNativeInboundItemCommand(projectId, " Ship it ", "body", "native-42", " Ada "),
            TestContext.Current.CancellationToken);

        inserted.ShouldNotBeNull();
        inserted.Provider.ShouldBe(TicketProvider.Native);
        inserted.ExternalId.ShouldBe("native-42");
        inserted.Title.ShouldBe("Ship it");
        inserted.Author.ShouldBe("Ada");
        view.Status.ShouldBe("Claimed");
        view.RunId.ShouldBe(runId.Value);
        view.ExternalId.ShouldBe("native-42");
        await runLauncher.Received(1).LaunchAsync(projectId, Arg.Any<Domain.Connections.SourceConnection>(), inserted, Arg.Any<CancellationToken>());
        await store.Received(1).TryMarkClaimedAsync(inserted.Id, runId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an empty external id, when Handle runs, then a native- prefix id is generated")]
    public async Task GenerateExternalIdWhenMissingAsync()
    {
        store.TryInsertTicketAsync(Arg.Any<InboundItem>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo => callInfo.Arg<InboundItem>());
        runLauncher.LaunchAsync(Arg.Any<ProjectId>(), Arg.Any<Domain.Connections.SourceConnection>(), Arg.Any<InboundItem>(), Arg.Any<CancellationToken>())
            .Returns(RunId.New());
        store.TryMarkClaimedAsync(Arg.Any<InboundItemId>(), Arg.Any<RunId>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateNativeInboundItemHandler(
            store,
            runLauncher,
            clock,
            new CreateNativeInboundItemValidator(),
            NullLogger<CreateNativeInboundItemHandler>.Instance);

        var view = await handler.HandleAsync(
            new CreateNativeInboundItemCommand(ProjectId.New(), "Ship", "body", "", null),
            TestContext.Current.CancellationToken);

        view.ExternalId.ShouldStartWith("native-");
        view.ExternalId.Length.ShouldBeGreaterThan("native-".Length);
    }

    [Fact(DisplayName = "Given a duplicate active ticket, when Handle runs, then InboundItemConflictException is thrown")]
    public async Task RefuseDuplicateAsync()
    {
        store.TryInsertTicketAsync(Arg.Any<InboundItem>(), Arg.Any<CancellationToken>()).Returns((InboundItem?)null);
        var handler = new CreateNativeInboundItemHandler(
            store,
            runLauncher,
            clock,
            new CreateNativeInboundItemValidator(),
            NullLogger<CreateNativeInboundItemHandler>.Instance);

        await Should.ThrowAsync<InboundItemConflictException>(
            () => handler.HandleAsync(
                new CreateNativeInboundItemCommand(ProjectId.New(), "Ship", "body", "dup", null),
                TestContext.Current.CancellationToken));
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
