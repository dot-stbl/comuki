using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Modules.Projects.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Cache-refresher fallback (issue Q27 / v1.1). When the underlying
/// store (DB today, Redis when the planned
/// <c>DistributedProjectSettingsCache</c> lands) is unreachable, the
/// refresher no longer retries and silently waits — it falls back to
/// the last-known snapshot held in the refresher, each row with a hard
/// 30s TTL. After the TTL elapses the snapshot is dropped so the
/// cache eventually goes cold rather than serving
/// indefinitely-stale data.
/// <para>
/// The test seam is <c>SeedFallback</c> on the production refresher —
/// internal, only callable from this assembly. The successful-pass
/// path is exercised by the production host against a real DB and
/// covered by the integration suite; this unit test focuses on the
/// fallback semantics, which are the new behaviour.
/// </para>
/// </summary>
public sealed class ProjectSettingsCacheRefresherShould
{
    [Fact(DisplayName = "Given a seeded fallback snapshot, when the DB stays down, then the cache is re-warmed from the snapshot")]
    public async Task FallbackToLastKnownSnapshotAsync()
    {
        // Issue Q27 / v1.1: the refresher must serve the last-known
        // snapshot when the underlying store is unreachable.
        var refresher = NewRefresher(out var cache, out _, out var logger);

        var first = ProjectSettings.CreateDefaults(ProjectId.New(), DateTimeOffset.UtcNow);
        var second = ProjectSettings.CreateDefaults(ProjectId.New(), DateTimeOffset.UtcNow);
        refresher.SeedFallback([first, second]);

        await RunOneCycleAsync(refresher);

        cache.Get(first.ProjectId).ShouldNotBeNull();
        cache.Get(second.ProjectId).ShouldNotBeNull();

        logger.Records.ShouldContain(static entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("in-memory fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Given a fallback older than the TTL, when the DB stays down, then the cache entry is dropped and reads return null")]
    public async Task FallbackDropsExpiredSnapshotsAsync()
    {
        // Issue Q27 / v1.1: the fallback must not extend staleness
        // beyond the 30s TTL — after that the cache goes cold and
        // reads start returning null rather than serving
        // indefinitely-stale data.
        var refresher = NewRefresher(out var cache, out var clock, out _);

        var only = ProjectSettings.CreateDefaults(ProjectId.New(), DateTimeOffset.UtcNow);
        refresher.SeedFallback([only]);

        clock.UtcNow += ProjectSettingsCacheRefresher.FallbackTtl + TimeSpan.FromSeconds(1);
        await RunOneCycleAsync(refresher);

        cache.Get(only.ProjectId).ShouldBeNull();
    }

    private static async Task RunOneCycleAsync(ProjectSettingsCacheRefresher refresher)
    {
        // Drive one ExecuteAsync cycle: the loop's outer try catches the
        // DbException and routes through the fallback path. Exercising
        // the public ExecuteAsync entry keeps the contract honest and
        // exercises the real catch block.
        using var cts = new CancellationTokenSource();
        await refresher.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await refresher.StopAsync(CancellationToken.None);
    }

    private static ProjectSettingsCacheRefresher NewRefresher(
        out ProjectSettingsCache cache,
        out TestableClock clock,
        out RecordingLogger<ProjectSettingsCacheRefresher> logger)
    {
        clock = new TestableClock { UtcNow = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero) };
        // The cache and the refresher share the test clock so the
        // cache's 30s TTL elapses on the same tick the test advances —
        // a wall-clock-driven cache would make the "expired snapshot"
        // assertion racy on a fast CI box.
        cache = new ProjectSettingsCache(new MemoryCache(new MemoryCacheOptions
        {
            Clock = new TestableSystemClock(clock),
        }));
        logger = new RecordingLogger<ProjectSettingsCacheRefresher>();
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        var dbFactory = new ThrowingDbContextFactory();
        return new ProjectSettingsCacheRefresher(dbFactory, scopeAccessor, cache, clock, logger);
    }

    /// <summary>DB factory that throws on every call — drives the
    /// refresher into the fallback path on every cycle.</summary>
    private sealed class ThrowingDbContextFactory : IDbContextFactory<Infrastructure.Persistence.ProjectsDbContext>
    {
        public Infrastructure.Persistence.ProjectsDbContext CreateDbContext()
        {
            throw new TestDbException("simulated store outage");
        }

        public Task<Infrastructure.Persistence.ProjectsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }

    /// <summary>Test-only <see cref="TimeProvider"/> that advances
    /// only when the test says so.</summary>
    private sealed class TestableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    /// <summary>
    /// Adapter that bridges a <see cref="TimeProvider"/> to the
    /// <see cref="Microsoft.Extensions.Internal.ISystemClock"/> the
    /// <see cref="MemoryCache"/> uses for TTL. The cache advances in
    /// lock-step with the test clock.
    /// </summary>
    private sealed class TestableSystemClock(TimeProvider source) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => source.GetUtcNow();
    }

    /// <summary>Test-only <see cref="DbException"/> — the production
    /// catch narrows to <see cref="DbException"/>, so any concrete
    /// subtype routes through the fallback path.</summary>
    private sealed class TestDbException(string message) : System.Data.Common.DbException(message);

    /// <summary>In-process logger that captures every entry — used to
    /// assert that the fallback path emitted the expected warning.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> records = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Records => records;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return new NoopDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            records.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
