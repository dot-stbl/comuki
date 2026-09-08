using Comuki.Modules.Intake.Application.Inbox;
using Comuki.Modules.Intake.Application.Options;
using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Inbox read path: <see cref="InboxCatalogReader"/> turns an external
/// connection id into a page of the provider's issue catalog. Unknown
/// connection id and unknown provider key both surface as
/// <see cref="SourceConnectionNotFoundException"/>.
/// </summary>
public sealed class InboxCatalogReaderShould
{
    [Fact(DisplayName = "Given a registered connection and a provider, when FetchCatalogAsync runs, then the catalog page is mapped to IntakeTicketView and returned")]
    public async Task FetchCatalogReturnsMappedPageAsync()
    {
        var connection = NewConnection(TicketProvider.GitHub);
        var store = Substitute.For<IIntakeStore>();
        _ = store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>())
            .Returns(connection);
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var pendingTicket = IncomingTicket.Create(
            connection.ProjectId,
            TicketProvider.GitHub,
            "owner/repo#481",
            "issue title",
            "issue body",
            "alice",
            "https://example.com/481",
            "owner/repo",
            ["bug"],
            InboundTicketKind.Issue,
            now);
        _ = provider.FetchCatalogAsync(connection, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([pendingTicket]);
        var registry = new TicketProviderRegistry([provider], []);
        var reader = NewReader(store, registry);

        var views = await reader.FetchCatalogAsync(connection.Id, page: 2, TestContext.Current.CancellationToken);

        views.Count.ShouldBe(1);
        views[0].Source.ShouldBe(TicketProviderKeys.GitHub);
        views[0].ExternalId.ShouldBe("owner/repo#481");
        views[0].Title.ShouldBe("issue title");
        views[0].Url.ShouldBe("https://example.com/481");
        views[0].ProjectId.ShouldBe(connection.ProjectId.Value);
        await provider.Received(1).FetchCatalogAsync(connection, 2, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unknown connection id, when FetchCatalogAsync runs, then SourceConnectionNotFoundException is thrown")]
    public async Task UnknownConnectionThrowsSourceConnectionNotFoundAsync()
    {
        var store = Substitute.For<IIntakeStore>();
        _ = store.FindConnectionAsync(Arg.Any<SourceConnectionId>(), Arg.Any<CancellationToken>())
            .Returns((SourceConnection?)null);
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        var registry = new TicketProviderRegistry([provider], []);
        var reader = NewReader(store, registry);
        var unknownId = SourceConnectionId.New();

        var exception = await Should.ThrowAsync<SourceConnectionNotFoundException>(
            async () => await reader.FetchCatalogAsync(unknownId, page: 1, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(unknownId.ToString());
    }

    [Fact(DisplayName = "Given a connection whose provider is not registered, when FetchCatalogAsync runs, then SourceConnectionNotFoundException is thrown")]
    public async Task UnknownProviderKeyThrowsSourceConnectionNotFoundAsync()
    {
        var connection = NewConnection(TicketProvider.Jira);
        var store = Substitute.For<IIntakeStore>();
        _ = store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>())
            .Returns(connection);
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        var registry = new TicketProviderRegistry([provider], []);
        var reader = NewReader(store, registry);

        var exception = await Should.ThrowAsync<SourceConnectionNotFoundException>(
            async () => await reader.FetchCatalogAsync(connection.Id, page: 1, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(connection.Id.ToString());
    }

    [Fact(DisplayName = "Given a page less than one, when FetchCatalogAsync runs, then the provider is called with page one (Math.Max guard)")]
    public async Task NegativePageIsClampedToOneAsync()
    {
        var connection = NewConnection(TicketProvider.GitHub);
        var store = Substitute.For<IIntakeStore>();
        _ = store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>())
            .Returns(connection);
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        _ = provider.FetchCatalogAsync(connection, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var registry = new TicketProviderRegistry([provider], []);
        var reader = NewReader(store, registry);

        await reader.FetchCatalogAsync(connection.Id, page: 0, TestContext.Current.CancellationToken);

        await provider.Received(1).FetchCatalogAsync(connection, 1, Arg.Any<CancellationToken>());
    }

    private static SourceConnection NewConnection(TicketProvider provider)
    {
        return SourceConnection.Create(
            ProjectId.New(),
            provider,
            "test-connection",
            "{}",
            "TEST_SECRET_ENV_REF",
            Guid.NewGuid().ToString("N"),
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    }

    private static InboxCatalogReader NewReader(IIntakeStore store, TicketProviderRegistry registry)
    {
        return new InboxCatalogReader(
            store,
            registry,
            Substitute.For<IOptions<IntakeOptions>>());
    }
}
