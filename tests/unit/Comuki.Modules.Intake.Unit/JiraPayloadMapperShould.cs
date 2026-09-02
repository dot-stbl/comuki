using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.Jira;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Jira payload mapper over real payload fixtures.</summary>
public sealed class JiraPayloadMapperShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;
    private const string Site = "https://acme.atlassian.net";

    [Fact(DisplayName = "Given a jira:issue_created payload, when normalized, then the ticket maps with the browse URL and project key")]
    public async Task MapIssueCreatedAsync()
    {
        var ticket = JiraPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("jira-issue-created.json"), Site, projectId, now);

        ticket.ShouldNotBeNull();
        ticket.Provider.ShouldBe(TicketProvider.Jira);
        ticket.ExternalId.ShouldBe("COM-9");
        ticket.Title.ShouldBe("Migration drops indexes");
        ticket.Author.ShouldBe("dave");
        ticket.Url.ShouldBe("https://acme.atlassian.net/browse/COM-9");
        ticket.ProjectKey.ShouldBe("COM");
        ticket.Labels.ShouldBe(["db", "comuki"], ignoreOrder: false);
    }

    [Fact(DisplayName = "Given a jira:issue_deleted payload, when normalized, then it skips (null)")]
    public async Task SkipDeletedEventAsync()
    {
        var ticket = JiraPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("jira-issue-deleted.json"), Site, projectId, now);

        ticket.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a degenerate payload, when normalized, then it answers null")]
    [InlineData("")]
    [InlineData(/*lang=json,strict*/ """{"webhookEvent": "jira:issue_created"}""")]
    public void SkipDegeneratePayloads(string payload)
    {
        JiraPayloadMapper.ToTicket(System.Text.Encoding.UTF8.GetBytes(payload), Site, projectId, now).ShouldBeNull();
    }
}
