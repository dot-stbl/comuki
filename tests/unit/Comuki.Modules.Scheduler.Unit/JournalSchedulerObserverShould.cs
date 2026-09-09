using System.Text.Json;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Infrastructure.Observers;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// Journal side-channel: every scheduler fire appends one
/// <c>scheduler.job_fired</c> entry to the orchestration
/// <c>run_events</c> journal so downstream subscribers (realtime hub,
/// audit) see the fire.
/// </summary>
public sealed class JournalSchedulerObserverShould
{
    private static readonly DateTimeOffset anchorTime =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a fire, when the observer is notified, then one scheduler.job_fired entry is appended")]
    public async Task AppendsJournalEntryAsync()
    {
        var journal = Substitute.For<IRunJournal>();
        var observer = new JournalSchedulerObserver(JournalScopeFactory.Build(journal));
        var jobId = new ScheduledJobId(Guid.CreateVersion7());
        var projectId = new ProjectId(Guid.CreateVersion7());
        var runId = new RunId(Guid.CreateVersion7());

        await observer.OnJobFiredAsync(
            jobId,
            projectId,
            profileKey: "ops-sentry",
            runId,
            anchorTime,
            TestContext.Current.CancellationToken);

        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(entry =>
                entry.Type == "scheduler.job_fired"
                && entry.RunId == runId
                && entry.OccurredAt == anchorTime),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a fire, when the observer is notified, then the payload carries jobId, projectId, profileKey and firedAt")]
    public async Task CarriesPayloadFieldsAsync()
    {
        var journal = Substitute.For<IRunJournal>();
        var observer = new JournalSchedulerObserver(JournalScopeFactory.Build(journal));
        var jobId = new ScheduledJobId(Guid.CreateVersion7());
        var projectId = new ProjectId(Guid.CreateVersion7());
        var runId = new RunId(Guid.CreateVersion7());

        RunEventEntry captured = default!;
        await journal.AppendAsync(Arg.Do<RunEventEntry>(entry => captured = entry), Arg.Any<CancellationToken>());

        await observer.OnJobFiredAsync(
            jobId,
            projectId,
            profileKey: "ops-sentry",
            runId,
            anchorTime,
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        var payload = JsonSerializer.Deserialize<JsonElement>(captured.PayloadJson);
        payload.GetProperty("jobId").GetGuid().ShouldBe(jobId.Value);
        payload.GetProperty("projectId").GetGuid().ShouldBe(projectId.Value);
        payload.GetProperty("profileKey").GetString().ShouldBe("ops-sentry");
        payload.GetProperty("firedAt").GetDateTimeOffset().ShouldBe(anchorTime);
    }

    [Fact(DisplayName = "Given a journal append that throws, when the observer is notified, then the exception propagates")]
    public async Task JournalFailurePropagatesAsync()
    {
        var journal = Substitute.For<IRunJournal>();
        journal.AppendAsync(Arg.Any<RunEventEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("journal down"));
        var observer = new JournalSchedulerObserver(JournalScopeFactory.Build(journal));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            observer.OnJobFiredAsync(
                new ScheduledJobId(Guid.CreateVersion7()),
                new ProjectId(Guid.CreateVersion7()),
                profileKey: "ops-sentry",
                new RunId(Guid.CreateVersion7()),
                anchorTime,
                TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// Build helper for the journal observer tests: a single
/// <see cref="IServiceScope"/> backed by the supplied
/// <see cref="IRunJournal"/> substitute is enough — the observer under
/// test calls <c>scopeFactory.CreateAsyncScope()</c> and asks the scope
/// for <c>IRunJournal</c>, so a stub that yields the same scope on every
/// call is sufficient (a real <see cref="ServiceCollection"/> would, but
/// here we side-step that with a thin NSubstitute pair).
/// </summary>
file static class JournalScopeFactory
{
    /// <summary>Builds the scope-factory stub for the supplied journal mock.</summary>
    /// <param name="journal">Mock the scope will resolve.</param>
    /// <returns></returns>
    public static IServiceScopeFactory Build(IRunJournal journal)
    {
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(IRunJournal)).Returns(journal);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateAsyncScope().Returns(scope);
        return factory;
    }
}
