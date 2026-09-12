using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Hosted;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="KnowledgeIngestBackgroundService"/>:
/// the periodic doc worker must log its start / stop lifecycle and
/// honour cancellation between iterations. The ingest path itself
/// (per-source <see cref="Application.IKnowledgeIngestor"/> scope) lands in a later
/// slice — these tests cover the v0 heartbeat.
/// </summary>
public sealed class KnowledgeIngestBackgroundServiceShould
{
    private static readonly TimeSpan startUpWindow = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "Given a service, when it is started and stopped, then the started and stopped logs are emitted")]
    public async Task StartAndStopLifecycleLogsAsync()
    {
        var logger = new ListLogger<KnowledgeIngestBackgroundService>();
        var service = NewService(logger, pollIntervalSeconds: 60);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(() => logger.Records.Any(record => record.Message.Contains("knowledge doc worker started")), startUpWindow, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        logger.Records.ShouldContain(record => record.Message.Contains("knowledge doc worker started"));
        logger.Records.ShouldContain(record => record.Message.Contains("knowledge doc worker stopped"));
    }

    [Fact(DisplayName = "Given PollIntervalSeconds = 0 in options, when the service starts, then it uses a 1-second floor")]
    public async Task PollIntervalIsClampedToMinimumOneSecondAsync()
    {
        var logger = new ListLogger<KnowledgeIngestBackgroundService>();
        var service = NewService(logger, pollIntervalSeconds: 0);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(() => logger.Records.Any(record => record.Message.Contains("knowledge doc worker started")), startUpWindow, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var started = logger.Records.First(record => record.Message.Contains("knowledge doc worker started"));
        started.Message.ShouldContain("1");
    }

    private static KnowledgeIngestBackgroundService NewService(
        ILogger<KnowledgeIngestBackgroundService> logger,
        int pollIntervalSeconds)
    {
        var options = new KnowledgeIngestOptions { PollIntervalSeconds = pollIntervalSeconds };
        return new KnowledgeIngestBackgroundService(Options.Create(options), logger);
    }

    /// <summary>
    /// Polls <paramref name="predicate"/> until it returns <c>true</c> or the
    /// timeout elapses — the BackgroundService starts as a fire-and-forget
    /// task, so we cannot <c>await</c> the "started" log synchronously.
    /// </summary>
    private static async Task WaitForAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }
}

/// <summary>
/// Minimal in-memory <see cref="ILogger{T}"/> for asserting log
/// emissions — captures messages into a list the test can introspect.
/// Avoids pulling <c>Microsoft.Extensions.Logging.Testing</c> just to
/// assert on a single lifecycle event.
/// </summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<LogRecord> Records { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Records.Add(new LogRecord(logLevel, formatter(state, exception)));
    }

    public sealed record LogRecord(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
