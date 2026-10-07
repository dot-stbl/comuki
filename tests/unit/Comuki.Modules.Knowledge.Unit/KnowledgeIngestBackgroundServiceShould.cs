using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Hosted;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="KnowledgeIngestComukiWorker"/>: the
/// periodic doc worker is now an <see cref="IComukiWorker"/>, so the
/// surface under test is the worker contract (Name, Schedule, and the
/// heartbeat result of <see cref="IComukiWorker.ExecuteAsync"/>) plus
/// the floor on <c>PollIntervalSeconds</c>. The BackgroundService
/// start/stop lifecycle is owned by the comuki worker registry now;
/// those assertions moved to <c>ComukiWorkerRegistryShould</c>.
/// </summary>
public sealed class KnowledgeIngestComukiWorkerShould
{
    [Fact(DisplayName = "Given a worker, when Name is read, then it returns knowledge-ingest")]
    public void NameIsStableForOperationalLookups()
    {
        var worker = NewWorker(NullLogger<KnowledgeIngestComukiWorker>.Instance, pollIntervalSeconds: 60);

        worker.Name.ShouldBe("knowledge-ingest");
    }

    [Fact(DisplayName = "Given PollIntervalSeconds = 60, when Schedule is read, then it returns a 60-second interval")]
    public void ScheduleReflectsPollIntervalSeconds()
    {
        var worker = NewWorker(NullLogger<KnowledgeIngestComukiWorker>.Instance, pollIntervalSeconds: 60);

        var schedule = worker.Schedule.ShouldBeOfType<WorkerSchedule.IntervalWorkerSchedule>();
        schedule.PollInterval.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact(DisplayName = "Given PollIntervalSeconds = 0 in options, when the worker is constructed, then the interval is clamped to 1 second")]
    public void PollIntervalIsClampedToMinimumOneSecond()
    {
        var worker = NewWorker(NullLogger<KnowledgeIngestComukiWorker>.Instance, pollIntervalSeconds: 0);

        var schedule = worker.Schedule.ShouldBeOfType<WorkerSchedule.IntervalWorkerSchedule>();
        schedule.PollInterval.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact(DisplayName = "Given a healthy worker, when ExecuteAsync is called, then it returns Ok with heartbeat detail")]
    public async Task ExecuteAsyncReturnsHeartbeatOkAsync()
    {
        var worker = NewWorker(NullLogger<KnowledgeIngestComukiWorker>.Instance, pollIntervalSeconds: 60);
        var context = new WorkerContext(
            new EmptyServiceProvider(),
            TimeProvider.System,
            NullLogger<KnowledgeIngestComukiWorker>.Instance);

        var result = await worker.ExecuteAsync(context, TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.Detail.ShouldBe("heartbeat");
    }

    private static KnowledgeIngestComukiWorker NewWorker(
        ILogger<KnowledgeIngestComukiWorker> logger,
        int pollIntervalSeconds)
    {
        var options = new KnowledgeIngestOptions { PollIntervalSeconds = pollIntervalSeconds };
        return new KnowledgeIngestComukiWorker(Options.Create(options), logger);
    }

    /// <summary>
    /// Minimal <see cref="IServiceProvider"/> for the worker's
    /// <see cref="WorkerContext"/> — the heartbeat path doesn't
    /// resolve anything, so an empty provider is enough.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
