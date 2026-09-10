using Comuki.Modules.Artifacts.Application.VisualArtifacts;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Artifacts.Unit;

/// <summary>
/// Truth-table of <see cref="VisualArtifactService.PublishFromWorkerAsync"/>:
/// content-type + size rejections are caught before ownership + storage;
/// the storage layer is invoked exactly once on the happy path; the
/// journal event carries the typed payload; the not-owner path is a
/// single round-trip with no MinIO call. The service is wired against
/// NSubstitute substitutes for the store, the work-item source and the
/// run journal.
/// </summary>
public sealed class VisualArtifactServiceShould
{
    private static readonly VisualArtifactId anyArtifactId = VisualArtifactId.New();

    private static readonly ProjectId anyProjectId = ProjectId.New();

    private static readonly RunId anyRunId = RunId.New();

    private static readonly WorkerId anyWorkerId = WorkerId.New();

    [Fact(DisplayName = "Given a valid png, when PublishFromWorkerAsync is called, then it uploads to MinIO and journals artifact.published")]
    public async Task HappyPathAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var ownership = new WorkItemOwnership(anyProjectId, anyRunId);
        var workItemId = Guid.NewGuid();
        var filename = "preview.png";
        var contentType = "image/png";
        var sizeBytes = 4096L;
        var body = new MemoryStream(new byte[sizeBytes]);
        var uploaded = NewArtifact(contentType, sizeBytes, filename);

        var store = Substitute.For<IVisualArtifactStore>();
        var workItemSource = Substitute.For<IWorkItemArtifactSource>();
        var journal = Substitute.For<IRunJournal>();

        workItemSource
            .FindOwnedAsync(workItemId, anyWorkerId, now, Arg.Any<CancellationToken>())
            .Returns(ownership);
        store
            .UploadVisualAsync(
                Arg.Is<VisualArtifactUploadRequest>(request =>
                    request.ProjectId == anyProjectId
                    && request.ContentType == contentType
                    && request.SizeBytes == sizeBytes
                    && request.Filename == filename
                    && request.CreatedBy == VisualArtifactCreatedByWire.Worker
                    && request.RunId == anyRunId.Value
                    && request.WorkItemId == workItemId),
                Arg.Any<CancellationToken>())
            .Returns(uploaded);

        var service = new VisualArtifactService(
            store,
            workItemSource,
            journal,
            new FixedClock(now),
            NullLogger<VisualArtifactService>.Instance);

        var outcome = await service.PublishFromWorkerAsync(
            workItemId,
            anyWorkerId,
            filename,
            contentType,
            sizeBytes,
            body,
            title: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var published = outcome.ShouldBeOfType<VisualArtifactPublishOutcome.PublishedRecord>();
        published.Artifact.ShouldBe(uploaded);

        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(entry =>
                entry.RunId == anyRunId
                && entry.Type == VisualArtifactEvents.Published
                && entry.OccurredAt == now),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unsupported MIME, when PublishFromWorkerAsync is called, then it returns UnsupportedMime and does not touch the store")]
    public async Task RejectsUnsupportedMimeAsync()
    {
        var workItemSource = Substitute.For<IWorkItemArtifactSource>();
        var store = Substitute.For<IVisualArtifactStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new VisualArtifactService(
            store,
            workItemSource,
            journal,
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<VisualArtifactService>.Instance);

        var outcome = await service.PublishFromWorkerAsync(
            Guid.NewGuid(),
            anyWorkerId,
            "evil.exe",
            "application/x-msdownload",
            100,
            new MemoryStream(new byte[100]),
            title: null,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.ShouldBeOfType<VisualArtifactPublishOutcome.UnsupportedMime>()
            .ContentType.ShouldBe("application/x-msdownload");
        await store.DidNotReceive().UploadVisualAsync(Arg.Any<VisualArtifactUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Given a payload larger than the per-mime cap, when PublishFromWorkerAsync is called, then it returns SizeExceeded and does not touch the store")]
    [InlineData("image/png", 6L * 1024 * 1024)]
    [InlineData("text/html", 2L * 1024 * 1024)]
    [InlineData("image/svg+xml", 2L * 1024 * 1024)]
    public async Task RejectsOversizedPayloadAsync(string contentType, long oversizedBytes)
    {
        var workItemSource = Substitute.For<IWorkItemArtifactSource>();
        var store = Substitute.For<IVisualArtifactStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new VisualArtifactService(
            store,
            workItemSource,
            journal,
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<VisualArtifactService>.Instance);

        var outcome = await service.PublishFromWorkerAsync(
            Guid.NewGuid(),
            anyWorkerId,
            "big.bin",
            contentType,
            oversizedBytes,
            new MemoryStream(new byte[1024]),
            title: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var rejected = outcome.ShouldBeOfType<VisualArtifactPublishOutcome.SizeExceeded>();
        rejected.ContentType.ShouldBe(contentType);
        rejected.SizeBytes.ShouldBe(oversizedBytes);
        await store.DidNotReceive().UploadVisualAsync(Arg.Any<VisualArtifactUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker that does not own the work item, when PublishFromWorkerAsync is called, then it returns NotOwned and does not touch the store")]
    public async Task RejectsUnownedWorkItemAsync()
    {
        var workItemSource = Substitute.For<IWorkItemArtifactSource>();
        workItemSource
            .FindOwnedAsync(Arg.Any<Guid>(), anyWorkerId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((WorkItemOwnership?)null);

        var store = Substitute.For<IVisualArtifactStore>();
        var journal = Substitute.For<IRunJournal>();
        var service = new VisualArtifactService(
            store,
            workItemSource,
            journal,
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<VisualArtifactService>.Instance);

        var outcome = await service.PublishFromWorkerAsync(
            Guid.NewGuid(),
            anyWorkerId,
            "preview.png",
            "image/png",
            1024,
            new MemoryStream(new byte[1024]),
            title: null,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.ShouldBeOfType<VisualArtifactPublishOutcome.NotOwned>();
        await store.DidNotReceive().UploadVisualAsync(Arg.Any<VisualArtifactUploadRequest>(), Arg.Any<CancellationToken>());
        await journal.DidNotReceive().AppendAsync(Arg.Any<RunEventEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an empty body, when PublishFromWorkerAsync is called, then the store receives a zero-byte upload")]
    public async Task AcceptsZeroBytePayloadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var workItemId = Guid.NewGuid();
        var ownership = new WorkItemOwnership(anyProjectId, anyRunId);
        var uploaded = NewArtifact("image/png", 0L, "empty.png");

        var store = Substitute.For<IVisualArtifactStore>();
        var workItemSource = Substitute.For<IWorkItemArtifactSource>();
        var journal = Substitute.For<IRunJournal>();

        workItemSource
            .FindOwnedAsync(workItemId, anyWorkerId, now, Arg.Any<CancellationToken>())
            .Returns(ownership);
        store
            .UploadVisualAsync(Arg.Any<VisualArtifactUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(uploaded);

        var service = new VisualArtifactService(
            store,
            workItemSource,
            journal,
            new FixedClock(now),
            NullLogger<VisualArtifactService>.Instance);

        var outcome = await service.PublishFromWorkerAsync(
            workItemId,
            anyWorkerId,
            "empty.png",
            "image/png",
            0,
            new MemoryStream([]),
            title: null,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.ShouldBeOfType<VisualArtifactPublishOutcome.PublishedRecord>();
        await store.Received(1).UploadVisualAsync(
            Arg.Is<VisualArtifactUploadRequest>(static request => request.SizeBytes == 0),
            Arg.Any<CancellationToken>());
    }

    private static VisualArtifact NewArtifact(string contentType, long sizeBytes, string filename)
    {
        return new VisualArtifact
        {
            Id = anyArtifactId.Value,
            ProjectId = anyProjectId.Value,
            Version = 1,
            Filename = filename,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = VisualArtifactCreatedByWire.Worker,
            RunId = anyRunId.Value,
        };
    }

    /// <summary>Deterministic clock for the service tests.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
