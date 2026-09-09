using Comuki.Modules.Artifacts.Application;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Artifacts.Unit;

/// <summary>
/// <see cref="RunArtifactPackagerService.PollOnceAsync"/>: empty
/// candidate streams short-circuit, successful bundles return the
/// outcome, exceptions thrown by a single bundle propagate (the driver
/// itself catches and logs at the <c>ExecuteAsync</c> layer — the per-
/// poll method lets the caller decide), and each candidate is bundled
/// inside its own DI scope so the inner <see cref="RunArtifactPackager"/>
/// instance is fresh per run.
/// </summary>
public sealed class RunArtifactPackagerServicePollOnceShould
{
    [Fact(DisplayName = "Given no candidates from the run source, when PollOnceAsync is called, then it returns an empty outcome list")]
    public async Task EmptyCandidatesProducesEmptyOutcomesAsync()
    {
        var runSource = Substitute.For<IRunArtifactRunSource>();
        runSource.ListUnbundledTerminalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyCandidatesAsync());

        using var fixture = NewFixture(runSource, out _, NewPackager);

        var outcomes = await fixture.Service.PollOnceAsync(TestContext.Current.CancellationToken);

        outcomes.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a candidate whose bundle succeeds, when PollOnceAsync runs, then the outcome is returned")]
    public async Task SuccessfulBundleIsReturnedAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;
        var candidate = new RunArtifactCandidate(runId, projectId);
        var expectedPointers = new[]
        {
            new ArtifactPointer("pins.json", new Uri("https://minio/b/pins.json"), 8, "application/json"),
        };

        var runSource = Substitute.For<IRunArtifactRunSource>();
        runSource.ListUnbundledTerminalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CandidatesAsync(candidate));

        var store = Substitute.For<IRunArtifactStore>();
        store.UploadAsync(Arg.Any<ProjectId>(), Arg.Any<RunId>(), Arg.Any<string>(),
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://minio/b/pins.json"));
        store.ListAsync(Arg.Any<ProjectId>(), Arg.Any<RunId>(), Arg.Any<CancellationToken>())
            .Returns(expectedPointers);

        var journal = Substitute.For<IRunArtifactJournalSource>();
        journal.ReadTerminalAsync(runId, Arg.Any<CancellationToken>())
            .Returns(new RunTerminalSnapshot(
                RunId: runId.Value,
                Status: "succeeded",
                OccurredAt: now,
                OriginWorkItemId: null,
                DetailJson: "{}"));

        var bundleStore = Substitute.For<IRunArtifactBundleStore>();
        bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>()).Returns(false);

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedClock(now),
            NullLogger<RunArtifactPackager>.Instance);
        using var fixture = NewFixture(runSource, out _, () => packager);

        var outcomes = await fixture.Service.PollOnceAsync(TestContext.Current.CancellationToken);

        outcomes.Count.ShouldBe(1);
        outcomes[0].RunId.ShouldBe(runId.Value);
        // pins.json + result.json — brief.json is gated on OriginWorkItemId,
        // which we left null. ObjectCount reflects the three artefact slots
        // the packager touches (brief / result / pins) and the snapshot
        // populates two of them.
        outcomes[0].ObjectCount.ShouldBe(2);
        outcomes[0].Pointers.ShouldBe(expectedPointers);
    }

    [Fact(DisplayName = "Given a candidate whose bundle throws, when PollOnceAsync runs, then the exception propagates from PollOnceAsync")]
    public async Task BundleExceptionPropagatesFromPollOnceAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var candidate = new RunArtifactCandidate(runId, projectId);

        var runSource = Substitute.For<IRunArtifactRunSource>();
        runSource.ListUnbundledTerminalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CandidatesAsync(candidate));

        var bundleStore = Substitute.For<IRunArtifactBundleStore>();
        bundleStore.IsBundledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("minio is down"));

        var packager = new RunArtifactPackager(
            Substitute.For<IRunArtifactStore>(),
            Substitute.For<IRunArtifactJournalSource>(),
            bundleStore,
            TimeProvider.System,
            NullLogger<RunArtifactPackager>.Instance);
        using var fixture = NewFixture(runSource, out _, () => packager);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await fixture.Service.PollOnceAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given two candidates, when PollOnceAsync runs, then each bundle runs in its own scope and resolves a fresh RunArtifactPackager instance")]
    public async Task EachBundleRunsInItsOwnScopeAsync()
    {
        var projectId = ProjectId.New();
        var first = new RunArtifactCandidate(RunId.New(), projectId);
        var second = new RunArtifactCandidate(RunId.New(), projectId);

        var runSource = Substitute.For<IRunArtifactRunSource>();
        runSource.ListUnbundledTerminalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CandidatesAsync(first, second));

        var instances = new List<RunArtifactPackager>();
        RunArtifactPackager Factory() { var p = NewPackager(); instances.Add(p); return p; }
        using var fixture = NewFixture(runSource, out _, Factory);

        await fixture.Service.PollOnceAsync(TestContext.Current.CancellationToken);

        // The packager is registered Scoped; each candidate opens a fresh
        // bundle scope, so the factory is invoked once per candidate and
        // produces a distinct instance.
        instances.Count.ShouldBe(2);
        ReferenceEquals(instances[0], instances[1]).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a discovery scope and bundle scopes, when PollOnceAsync runs, then the discovery scope is disposed before the first bundle scope is opened")]
    public async Task DiscoveryScopeDisposedBeforeBundleScopesOpenAsync()
    {
        var projectId = ProjectId.New();
        var firstCandidate = new RunArtifactCandidate(RunId.New(), projectId);
        var secondCandidate = new RunArtifactCandidate(RunId.New(), projectId);

        var runSource = Substitute.For<IRunArtifactRunSource>();
        runSource.ListUnbundledTerminalAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CandidatesAsync(firstCandidate, secondCandidate));

        using var fixture = NewTrackingFixture(runSource, out var tracker, NewPackager);

        await fixture.Service.PollOnceAsync(TestContext.Current.CancellationToken);

        tracker.ScopeEvents.ShouldNotBeEmpty();
        var firstDisposed = tracker.ScopeEvents
            .Select(static (entry, index) => (entry, index))
            .First(static pair => pair.entry.Kind == ScopeEventKind.Disposed).index;
        var firstBundleOpened = tracker.ScopeEvents
            .Select(static (entry, index) => (entry, index))
            .First(static pair => pair.entry.Kind == ScopeEventKind.Created && pair.entry.Phase == ScopePhase.Bundle).index;
        firstDisposed.ShouldBeLessThan(firstBundleOpened);
        tracker.BundleScopesCreated.ShouldBe(2);
    }

    private static RunArtifactPackager NewPackager()
    {
        return new RunArtifactPackager(
            Substitute.For<IRunArtifactStore>(),
            Substitute.For<IRunArtifactJournalSource>(),
            Substitute.For<IRunArtifactBundleStore>(),
            TimeProvider.System,
            NullLogger<RunArtifactPackager>.Instance);
    }

    private static TrackingTestFixture NewTrackingFixture(
        IRunArtifactRunSource runSource,
        out ScopeLifecycleTracker tracker,
        Func<RunArtifactPackager> packagerFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISubjectScopeAccessor>(new AsyncLocalSubjectScopeAccessor());
        services.AddArtifactsApplication();
        services.RemoveAll<IRunArtifactRunSource>();
        services.AddSingleton(runSource);
        services.RemoveAll<RunArtifactPackager>();
        services.AddScoped(_ => packagerFactory());
        var provider = services.BuildServiceProvider();
        var innerFactory = provider.GetRequiredService<IServiceScopeFactory>();
        tracker = new ScopeLifecycleTracker();
        var trackingFactory = new TrackingScopeFactory(innerFactory, tracker);
        var service = new RunArtifactPackagerService(
            trackingFactory,
            provider.GetRequiredService<ISubjectScopeAccessor>(),
            NullLogger<RunArtifactPackagerService>.Instance);
        return new TrackingTestFixture(provider, service);
    }

    private static TestFixture NewFixture(
        IRunArtifactRunSource runSource,
        out IDisposable sentinel,
        Func<RunArtifactPackager> packagerFactory)
    {
        sentinel = null!;
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISubjectScopeAccessor>(new AsyncLocalSubjectScopeAccessor());
        services.AddArtifactsApplication();
        // Replace the default Null run-source with our scripted one.
        services.RemoveAll<IRunArtifactRunSource>();
        services.AddSingleton(runSource);
        // Replace the Scoped packager registration with our factory so we
        // can observe the per-scope instance lifecycle (or pin a single
        // instance for the no-/one-candidate tests).
        services.RemoveAll<RunArtifactPackager>();
        services.AddScoped(_ => packagerFactory());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new RunArtifactPackagerService(
            scopeFactory,
            provider.GetRequiredService<ISubjectScopeAccessor>(),
            NullLogger<RunArtifactPackagerService>.Instance);
        return new TestFixture(provider, service);
    }

    private static async IAsyncEnumerable<RunArtifactCandidate> EmptyCandidatesAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<RunArtifactCandidate> CandidatesAsync(params RunArtifactCandidate[] seed)
    {
        await Task.CompletedTask;
        foreach (var candidate in seed)
        {
            yield return candidate;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    /// <summary>Disposes the underlying service provider when the test exits; the production
    /// service holds a scope factory over it and crashes if it is disposed too early.</summary>
    private sealed class TestFixture(ServiceProvider provider, RunArtifactPackagerService service) : IDisposable
    {
        public RunArtifactPackagerService Service { get; } = service;

        public void Dispose()
        {
            provider.Dispose();
        }
    }

    /// <summary>Test fixture wired through <see cref="TrackingScopeFactory"/> so a test can read the lifecycle of every scope the service opens and disposes.</summary>
    private sealed class TrackingTestFixture(ServiceProvider provider, RunArtifactPackagerService service) : IDisposable
    {
        public RunArtifactPackagerService Service { get; } = service;

        public void Dispose()
        {
            provider.Dispose();
        }
    }

    /// <summary>
    /// Captures the order of <see cref="IServiceScope"/> creations and
    /// disposals. The driver opens a discovery scope, disposes it, then
    /// opens one scope per candidate — the tracker lets a test assert
    /// the discovery scope's disposal happens strictly before the first
    /// bundle scope opens.
    /// </summary>
    private enum ScopePhase { Discovery, Bundle }

    private enum ScopeEventKind { Created, Disposed }

    private readonly record struct ScopeEvent(ScopeEventKind Kind, ScopePhase Phase, int Sequence);

    private sealed class ScopeLifecycleTracker
    {
        private readonly Lock gate = new();
        private readonly List<ScopeEvent> events = [];
        private int bundleCount;

        public IReadOnlyList<ScopeEvent> ScopeEvents
        {
            get
            {
                lock (gate)
                {
                    return [.. events];
                }
            }
        }

        public int BundleScopesCreated
        {
            get
            {
                lock (gate)
                {
                    return bundleCount;
                }
            }
        }

        public int RecordCreated(ScopePhase phase)
        {
            lock (gate)
            {
                var sequence = events.Count;
                events.Add(new ScopeEvent(ScopeEventKind.Created, phase, sequence));
                if (phase == ScopePhase.Bundle)
                {
                    bundleCount++;
                }
                return sequence;
            }
        }

        public void RecordDisposed(int sequence, ScopePhase phase)
        {
            lock (gate)
            {
                events.Add(new ScopeEvent(ScopeEventKind.Disposed, phase, sequence));
            }
        }
    }

    /// <summary>Scope-factory wrapper that records every CreateScope / Dispose on the wrapped inner factory.</summary>
    private sealed class TrackingScopeFactory(IServiceScopeFactory inner, ScopeLifecycleTracker tracker) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            // Discovery runs first (the driver opens exactly one
            // discovery scope and disposes it before any bundle
            // scopes). Bundle scopes follow, one per candidate.
            var phase = tracker.ScopeEvents.Any(static e => e.Kind == ScopeEventKind.Disposed)
                ? ScopePhase.Bundle
                : ScopePhase.Discovery;
            var sequence = tracker.RecordCreated(phase);
            var innerScope = inner.CreateScope();
            return new TrackingScope(innerScope, phase, sequence, tracker);
        }

        private sealed class TrackingScope(IServiceScope inner, ScopePhase phase, int sequence, ScopeLifecycleTracker tracker) : IServiceScope
        {
            private bool disposed;

            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                tracker.RecordDisposed(sequence, phase);
                inner.Dispose();
            }
        }
    }
}


