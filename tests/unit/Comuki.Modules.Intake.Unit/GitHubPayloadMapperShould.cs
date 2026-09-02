using System.Text;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.GitHub;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// GitHub payload mapper over real payload fixtures: opened/labeled
/// events map to tickets, pings and non-issues skip, malformed bodies
/// answer null (never throw).
/// </summary>
public sealed class GitHubPayloadMapperShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given an issues.opened payload, when normalized, then the ticket carries the fully-qualified external id and labels")]
    public async Task MapOpenedIssueAsync()
    {
        var ticket = GitHubPayloadMapper.ToTicket(await FixtureAsync("github-issue-opened.json"), projectId, now);

        ticket.ShouldNotBeNull();
        ticket.Provider.ShouldBe(TicketProvider.GitHub);
        ticket.ExternalId.ShouldBe("dot-stbl/comuki#481");
        ticket.Title.ShouldBe("Release checklist broken");
        ticket.Body.ShouldNotBeEmpty();
        ticket.Author.ShouldBe("alice");
        ticket.Url.ShouldBe("https://github.com/dot-stbl/comuki/issues/481");
        ticket.ProjectKey.ShouldBe("dot-stbl/comuki");
        ticket.Labels.ShouldBe(["bug", "comuki"], ignoreOrder: false);
    }

    [Fact(DisplayName = "Given an issues.labeled payload, when normalized, then it maps like an opened event")]
    public async Task MapLabeledIssueAsync()
    {
        var ticket = GitHubPayloadMapper.ToTicket(await FixtureAsync("github-issue-labeled.json"), projectId, now);

        ticket.ShouldNotBeNull();
        ticket.ExternalId.ShouldBe("dot-stbl/comuki#481");
        ticket.Labels.ShouldContain("comuki");
    }

    [Fact(DisplayName = "Given a ping payload, when normalized, then it skips (null)")]
    public async Task SkipPingAsync()
    {
        var ticket = GitHubPayloadMapper.ToTicket(await FixtureAsync("github-ping.json"), projectId, now);

        ticket.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a degenerate payload, when normalized, then it never throws and answers null")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData(/*lang=json,strict*/ """{"action": "opened"}""")]
    [InlineData(/*lang=json,strict*/ """{"action": "closed", "issue": {"number": 1}, "repository": {"full_name": "a/b"}}""")]
    public void SkipDegeneratePayloads(string payload)
    {
        GitHubPayloadMapper.ToTicket(Encoding.UTF8.GetBytes(payload), projectId, now).ShouldBeNull();
    }

    [Fact(DisplayName = "Given an external id, when parsed back, then owner/repo/number round-trip")]
    public void ParseExternalId()
    {
        GitHubPayloadMapper.ParseExternalId("dot-stbl/comuki#481").ShouldBe(("dot-stbl", "comuki", 481));
        GitHubPayloadMapper.ParseExternalId("malformed").ShouldBeNull();
        GitHubPayloadMapper.ParseExternalId("a/b#notanumber").ShouldBeNull();
    }

    internal static async Task<ReadOnlyMemory<byte>> FixtureAsync(string fileName)
    {
        var text = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName),
            TestContext.Current.CancellationToken);

        return Encoding.UTF8.GetBytes(text);
    }
}
