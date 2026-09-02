using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Yandex Tracker payload mapper over real payload fixtures.</summary>
public sealed class YandexTrackerPayloadMapperShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given an issue payload, when normalized, then the ticket maps with the queue project key and tags")]
    public async Task MapIssueAsync()
    {
        var ticket = YandexTrackerPayloadMapper.ToTicket(await GitHubPayloadMapperShould.FixtureAsync("tracker-issue-created.json"), projectId, now);

        ticket.ShouldNotBeNull();
        ticket.Provider.ShouldBe(TicketProvider.YandexTracker);
        ticket.ExternalId.ShouldBe("COMUKI-5");
        ticket.Title.ShouldBe("Import hangs on large files");
        ticket.Author.ShouldBe("carol");
        ticket.ProjectKey.ShouldBe("COMUKI");
        ticket.Labels.ShouldBe(["import", "comuki"], ignoreOrder: false);
    }

    [Theory(DisplayName = "Given a degenerate payload, when normalized, then it answers null")]
    [InlineData("")]
    [InlineData(/*lang=json,strict*/ """{"not_an_issue": {}}""")]
    [InlineData(/*lang=json,strict*/ """{"issue": {"summary": "no key"}}""")]
    public void SkipDegeneratePayloads(string payload)
    {
        YandexTrackerPayloadMapper.ToTicket(System.Text.Encoding.UTF8.GetBytes(payload), projectId, now).ShouldBeNull();
    }
}
