using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Integration.Queue;

/// <summary>
/// <see cref="IRunJournal"/> against real Postgres: appends round-trip, the
/// timeline pages oldest-first, and paging arguments are guarded.
/// </summary>
public sealed class RunJournalShould : QueueDatabase
{
    private static readonly DateTimeOffset baseTime = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given journal entries, when the timeline is read, then pages come back oldest first")]
    public async Task PageTimelineOldestFirstAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        var run = await SeedRunAsync();
        var runId = run.Id;

        for (var index = 0; index < 5; index++)
        {
            await journal.AppendAsync(new RunEventEntry(
                Guid.CreateVersion7(),
                runId,
                RunEventTypes.WorkerReported,
                $$"""{"seq":{{index}}}""",
                baseTime.AddSeconds(index)), cancellationToken);
        }

        var firstPage = await journal.ReadTimelineAsync(runId, page: 1, pageSize: 2, cancellationToken);
        var secondPage = await journal.ReadTimelineAsync(runId, page: 2, pageSize: 2, cancellationToken);
        var lastPage = await journal.ReadTimelineAsync(runId, page: 3, pageSize: 2, cancellationToken);

        firstPage.Count.ShouldBe(2);
        secondPage.Count.ShouldBe(2);
        lastPage.Count.ShouldBe(1);

        using var firstPayload = JsonDocument.Parse(firstPage[0].PayloadJson);
        firstPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(0);
        using var secondPayload = JsonDocument.Parse(secondPage[0].PayloadJson);
        secondPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(2);
        using var lastPayload = JsonDocument.Parse(lastPage[0].PayloadJson);
        lastPayload.RootElement.GetProperty("seq").GetInt32().ShouldBe(4);
    }

    [Fact(DisplayName = "Given a run with no journal, when the timeline is read, then the page is empty")]
    public async Task ReturnEmptyTimelineForUnknownRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();

        var timeline = await journal.ReadTimelineAsync(RunId.New(), page: 1, pageSize: 10, cancellationToken);

        timeline.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given timelines of two runs, when one is read, then the other run's entries stay out")]
    public async Task IsolateTimelinesBetweenRunsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();
        var runA = await SeedRunAsync();
        var runB = await SeedRunAsync();

        await journal.AppendAsync(new RunEventEntry(Guid.CreateVersion7(), runA.Id, RunEventTypes.RunStatusChanged, /*lang=json,strict*/ """{"seq":"a"}""", baseTime), cancellationToken);
        await journal.AppendAsync(new RunEventEntry(Guid.CreateVersion7(), runB.Id, RunEventTypes.RunStatusChanged, /*lang=json,strict*/ """{"seq":"b"}""", baseTime.AddSeconds(1)), cancellationToken);

        var timelineA = await journal.ReadTimelineAsync(runA.Id, page: 1, pageSize: 10, cancellationToken);

        var entry = timelineA.ShouldHaveSingleItem();
        using var payload = JsonDocument.Parse(entry.PayloadJson);
        payload.RootElement.GetProperty("seq").GetString().ShouldBe("a");
    }

    [Fact(DisplayName = "Given a non-positive page or page size, when the timeline is read, then it throws")]
    public async Task RejectInvalidPagingAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();

        _ = await Should.ThrowAsync<ArgumentException>(() => journal.ReadTimelineAsync(RunId.New(), page: 0, pageSize: 10, cancellationToken));
        _ = await Should.ThrowAsync<ArgumentException>(() => journal.ReadTimelineAsync(RunId.New(), page: 1, pageSize: 0, cancellationToken));
    }
}
