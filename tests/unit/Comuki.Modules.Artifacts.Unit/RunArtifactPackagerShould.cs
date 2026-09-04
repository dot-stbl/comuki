using Comuki.Modules.Artifacts.Application;
using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Domain;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Artifacts.Unit;

/// <summary>
/// Truth-table of <see cref="RunArtifactPackager.BundleAsync"/>:
/// the four terminal statuses trigger bundling, the three in-flight
/// statuses are skipped, and the already-bundled run is a no-op. The
/// packager is wired against NSubstitute substitutes for the artifact
/// store, the journal source and the bookkeeping store — the assertions
/// are on the calls it made (or did not) and on the returned outcome.
/// </summary>
public sealed class RunArtifactPackagerShould
{
    [Theory(DisplayName = "Given a terminal status, when BundleAsync is called, then it uploads the bundle and records the row")]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("escalated")]
    public async Task BundleTerminalStatusAsync(string terminalStatus)
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var workItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var store = Substitute.For<IRunArtifactStore>();
        var journal = Substitute.For<IRunArtifactJournalSource>();
        var bundleStore = Substitute.For<IRunArtifactBundleStore>();

        _ = bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>())
            .Returns(false);
        _ = journal.ReadTerminalAsync(runId, Arg.Any<CancellationToken>())
            .Returns(new RunTerminalSnapshot(
                RunId: runId.Value,
                Status: terminalStatus,
                OccurredAt: now,
                OriginWorkItemId: workItemId,
                DetailJson: /*lang=json,strict*/ """{"summary":"done"}"""));
        _ = journal.ReadWorkItemBriefAsync(workItemId, Arg.Any<CancellationToken>())
            .Returns(/*lang=json,strict*/ """{"goal":"build a thing"}""");
        _ = store.ListAsync(projectId, runId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new ArtifactPointer("brief.json", new Uri("https://minio/b/brief.json"), 10, "application/json"),
                new ArtifactPointer("result.json", new Uri("https://minio/b/result.json"), 12, "application/json"),
                new ArtifactPointer("pins.json", new Uri("https://minio/b/pins.json"), 8, "application/json"),
            ]);

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedTimeProvider(now),
            NullLogger<RunArtifactPackager>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcome = await packager.BundleAsync(
            new RunArtifactCandidate(runId, projectId),
            cancellationToken);

        outcome.ShouldNotBeNull();
        outcome.RunId.ShouldBe(runId.Value);
        outcome.ObjectCount.ShouldBe(3);

        // Three upload calls: brief, result, pins.
        await store.Received(1).UploadAsync(
            projectId, runId, "brief.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
        await store.Received(1).UploadAsync(
            projectId, runId, "result.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
        await store.Received(1).UploadAsync(
            projectId, runId, "pins.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());

        // Bookkeeping row recorded with the terminal status.
        await bundleStore.Received(1).RecordAsync(
            Arg.Is<RunArtifactBundle>(bundle =>
                bundle.RunId == runId.Value
                && bundle.ProjectId == projectId.Value
                && bundle.Status == terminalStatus
                && bundle.ObjectCount == 3),
            Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Given an in-flight status, when BundleAsync is called, then it does not upload or record")]
    [InlineData("queued")]
    [InlineData("running")]
    [InlineData("waiting")]
    public async Task SkipInFlightStatusAsync(string inFlightStatus)
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var store = Substitute.For<IRunArtifactStore>();
        var journal = Substitute.For<IRunArtifactJournalSource>();
        var bundleStore = Substitute.For<IRunArtifactBundleStore>();

        _ = bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>())
            .Returns(false);
        _ = journal.ReadTerminalAsync(runId, Arg.Any<CancellationToken>())
            .Returns(new RunTerminalSnapshot(
                RunId: runId.Value,
                Status: inFlightStatus,
                OccurredAt: now,
                OriginWorkItemId: null,
                DetailJson: null));

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedTimeProvider(now),
            NullLogger<RunArtifactPackager>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcome = await packager.BundleAsync(
            new RunArtifactCandidate(runId, projectId),
            cancellationToken);

        outcome.ShouldBeNull();
        await store.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default, default!, default!, default!, cancellationToken);
        await bundleStore.DidNotReceiveWithAnyArgs().RecordAsync(default!, cancellationToken);
    }

    [Fact(DisplayName = "Given an already-bundled run, when BundleAsync is called, then it does not re-upload or re-record")]
    public async Task SkipAlreadyBundledAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var store = Substitute.For<IRunArtifactStore>();
        var journal = Substitute.For<IRunArtifactJournalSource>();
        var bundleStore = Substitute.For<IRunArtifactBundleStore>();

        _ = bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>())
            .Returns(true);

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedTimeProvider(now),
            NullLogger<RunArtifactPackager>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcome = await packager.BundleAsync(
            new RunArtifactCandidate(runId, projectId),
            cancellationToken);

        outcome.ShouldBeNull();
        await journal.DidNotReceiveWithAnyArgs().ReadTerminalAsync(default, cancellationToken);
        await store.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default, default!, default!, default!, cancellationToken);
    }

    [Fact(DisplayName = "Given no terminal snapshot yet, when BundleAsync is called, then it is a no-op")]
    public async Task SkipWhenNoTerminalSnapshotAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();

        var store = Substitute.For<IRunArtifactStore>();
        var journal = Substitute.For<IRunArtifactJournalSource>();
        var bundleStore = Substitute.For<IRunArtifactBundleStore>();

        _ = bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>())
            .Returns(false);
        _ = journal.ReadTerminalAsync(runId, Arg.Any<CancellationToken>())
            .Returns((RunTerminalSnapshot?)null);

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<RunArtifactPackager>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcome = await packager.BundleAsync(
            new RunArtifactCandidate(runId, projectId),
            cancellationToken);

        outcome.ShouldBeNull();
        await store.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default, default!, default!, default!, cancellationToken);
    }

    [Fact(DisplayName = "Given a system-driven terminal (no work item), when BundleAsync, then only pins.json is uploaded")]
    public async Task BundleSystemTerminalWithoutWorkItemAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var store = Substitute.For<IRunArtifactStore>();
        var journal = Substitute.For<IRunArtifactJournalSource>();
        var bundleStore = Substitute.For<IRunArtifactBundleStore>();

        _ = bundleStore.IsBundledAsync(runId.Value, Arg.Any<CancellationToken>())
            .Returns(false);
        _ = journal.ReadTerminalAsync(runId, Arg.Any<CancellationToken>())
            .Returns(new RunTerminalSnapshot(
                RunId: runId.Value,
                Status: "cancelled",
                OccurredAt: now,
                OriginWorkItemId: null,
                DetailJson: null));
        _ = store.ListAsync(projectId, runId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new ArtifactPointer("pins.json", new Uri("https://minio/b/pins.json"), 8, "application/json"),
            ]);

        var packager = new RunArtifactPackager(
            store,
            journal,
            bundleStore,
            new FixedTimeProvider(now),
            NullLogger<RunArtifactPackager>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcome = await packager.BundleAsync(
            new RunArtifactCandidate(runId, projectId),
            cancellationToken);

        outcome.ShouldNotBeNull();
        outcome.ObjectCount.ShouldBe(1);

        await store.Received(1).UploadAsync(
            projectId, runId, "pins.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
        await store.DidNotReceive().UploadAsync(
            projectId, runId, "brief.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
        await store.DidNotReceive().UploadAsync(
            projectId, runId, "result.json", Arg.Any<Stream>(), "application/json", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given AddArtifactsApplication, when resolved, then defaults and singleton services are registered")]
    public void RegisterApplicationDefaults()
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        // RunArtifactPackagerService now depends on ISubjectScopeAccessor to
        // declare the per-cycle AsSystem scope around the bundle phase
        // (the per-scope refactor in PollOnceAsync moved the scope out of
        // the discovery stream and into the per-candidate bundle scope).
        // The test only verifies registration, not subject-scope behaviour,
        // so the no-op stub is enough to satisfy DI.
        _ = services.AddSingleton<Shared.Kernel.Scoping.ISubjectScopeAccessor>(
            new Shared.Kernel.Scoping.AsyncLocalSubjectScopeAccessor());
        _ = services.AddArtifactsApplication();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunArtifactBundleStore>().ShouldBeOfType<NullRunArtifactBundleStore>();
        provider.GetRequiredService<RunArtifactPackager>().ShouldNotBeNull();
        provider.GetRequiredService<RunArtifactPackagerService>().ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given an empty path, when BuildObjectKey is called, then it throws")]
    public void RejectEmptyRelativePathAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();

        Should.Throw<ArgumentException>(() =>
            MinioRunArtifactStoreHelpers.BuildObjectKeyPublic(projectId, runId, ""));
    }

    [Fact(DisplayName = "Given a traversal path, when BuildObjectKey is called, then it throws")]
    public void RejectTraversalPathAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();

        Should.Throw<ArgumentException>(() =>
            MinioRunArtifactStoreHelpers.BuildObjectKeyPublic(projectId, runId, "../escape"));
    }

    [Fact(DisplayName = "Given a leading-slash path, when BuildObjectKey is called, then it throws")]
    public void RejectLeadingSlashPathAsync()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();

        Should.Throw<ArgumentException>(() =>
            MinioRunArtifactStoreHelpers.BuildObjectKeyPublic(projectId, runId, "/leading"));
    }
}

/// <summary>Deterministic <see cref="TimeProvider"/> for the packager test.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly DateTimeOffset now = now;

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
