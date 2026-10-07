using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Integrations.Infrastructure.Admission;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Integrations.Unit;

/// <summary>
/// Profile-router decisions: per-connection override, PR-kind default,
/// issue-kind default, native (null connection) fallback, and tolerant
/// parsing of a broken settings json.
/// </summary>
public sealed class IntegrationProfileRouterShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 3, 14, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a PR-kind ticket with no settings, then pr-review is returned")]
    public void DefaultForPullRequest()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.PullRequest);

        router.ResolveProfileKey(connection: null, ticket).ShouldBe("pr-review");
    }

    [Fact(DisplayName = "Given an issue-kind ticket with no settings, then the issue default is returned")]
    public void DefaultForIssue()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.Issue);

        router.ResolveProfileKey(connection: null, ticket).ShouldBe("general");
    }

    [Fact(DisplayName = "Given a connection with a profileKey override, then the override wins regardless of kind")]
    public void OverrideWins()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.PullRequest);
        var connection = Connection(/*lang=json,strict*/ """{"profileKey": "explore-readonly"}""");

        router.ResolveProfileKey(connection, ticket).ShouldBe("explore-readonly");
    }

    [Fact(DisplayName = "Given a connection with a malformed profileKey, then the kind default is returned (tolerant parse)")]
    public void MalformedOverrideFallsBack()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.Issue);
        var connection = Connection(/*lang=json,strict*/ """{"profileKey": 42}""");

        router.ResolveProfileKey(connection, ticket).ShouldBe("general");
    }

    [Fact(DisplayName = "Given a connection with broken json, then the kind default is returned (tolerant parse)")]
    public void BrokenJsonFallsBack()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.PullRequest);
        var connection = Connection("{not valid json");

        router.ResolveProfileKey(connection, ticket).ShouldBe("pr-review");
    }

    [Fact(DisplayName = "Given a connection with empty profileKey, then the kind default is returned")]
    public void EmptyOverrideFallsBack()
    {
        var router = new IntegrationProfileRouter("general");
        var ticket = Ticket(InboundItemKind.PullRequest);
        var connection = Connection(/*lang=json,strict*/ """{"profileKey": ""}""");

        router.ResolveProfileKey(connection, ticket).ShouldBe("pr-review");
    }

    private static SourceConnection Connection(string settingsJson)
    {
        return SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "test",
            settingsJson,
            "HOOK",
            "key1234567890abcd",
            now);
    }

    private static InboundItem Ticket(InboundItemKind kind)
    {
        return InboundItem.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "dot-stbl/comuki#1",
            "title",
            string.Empty,
            "alice",
            "https://example.com/1",
            "dot-stbl/comuki",
            ["bug"],
            kind,
            now);
    }
}
