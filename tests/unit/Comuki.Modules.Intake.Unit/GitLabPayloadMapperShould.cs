using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.GitLab;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>GitLab payload mapper over real payload fixtures.</summary>
public sealed class GitLabPayloadMapperShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given an issue open payload, when normalized, then the ticket maps with the path-qualified external id")]
    public async Task MapOpenIssueAsync()
    {
        var ticket = GitLabPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("gitlab-issue-open.json"), projectId, now);

        ticket.ShouldNotBeNull();
        ticket.Provider.ShouldBe(TicketProvider.GitLab);
        ticket.ExternalId.ShouldBe("acme/platform#12");
        ticket.Title.ShouldBe("Deploy fails at midnight");
        ticket.Author.ShouldBe("bob");
        ticket.ProjectKey.ShouldBe("acme/platform");
        ticket.Labels.ShouldBe(["infra", "comuki"], ignoreOrder: false);
    }

    [Fact(DisplayName = "Given an issue update payload, when normalized, then the fresh labels land")]
    public async Task MapUpdateIssueAsync()
    {
        var ticket = GitLabPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("gitlab-issue-update.json"), projectId, now);

        ticket.ShouldNotBeNull();
        ticket.ExternalId.ShouldBe("acme/platform#12");
        ticket.Labels.ShouldContain("flaky");
    }

    [Fact(DisplayName = "Given a push payload, when normalized, then it skips (null)")]
    public async Task SkipPushEventAsync()
    {
        var ticket = GitLabPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("gitlab-push.json"), projectId, now);

        ticket.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a degenerate payload, when normalized, then it answers null")]
    [InlineData("")]
    [InlineData("}{")]
    [InlineData(/*lang=json,strict*/ """{"object_kind": "issue"}""")]
    public void SkipDegeneratePayloads(string payload)
    {
        GitLabPayloadMapper.ToTicket(System.Text.Encoding.UTF8.GetBytes(payload), projectId, now).ShouldBeNull();
    }
}
