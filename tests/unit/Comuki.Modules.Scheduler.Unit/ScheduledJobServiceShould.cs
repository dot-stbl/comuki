using System.Globalization;
using Comuki.Modules.Scheduler.Application.Jobs;
using Comuki.Modules.Scheduler.Application.Ports;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// Service-layer façade over <see cref="IScheduledJobStore"/>: validation,
/// the create / update / delete / get paths, and the typed exceptions that
/// surface when the cron is malformed or the job is missing. The real
/// FluentValidation validators back the structural checks; the store is
/// scripted via NSubstitute.
/// </summary>
public sealed class ScheduledJobServiceShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-06T00:00:00Z",
        CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given a valid cron expression, when CreateAsync is called, then the job is persisted and a view is returned")]
    public async Task CreateAsyncPersistsJobAsync()
    {
        var store = Substitute.For<IScheduledJobStore>();
        var service = NewService(store);
        var projectId = new ProjectId(Guid.CreateVersion7());

        var view = await service.CreateAsync(
            new CreateScheduledJobCommand(
                ProjectId: projectId,
                CronExpression: "*/5 * * * *",
                ProfileKey: "general",
                BriefJson: "{}",
                RunOnOnceAt: null,
                Enabled: true),
            TestContext.Current.CancellationToken);

        view.CronExpression.ShouldBe("*/5 * * * *");
        view.ProfileKey.ShouldBe("general");
        view.ProjectId.ShouldBe(projectId.Value);
        view.Enabled.ShouldBeTrue();
        await store.Received(1).AddAsync(
            Arg.Is<ScheduledJob>(job => job.CronExpression == "*/5 * * * *"
                && job.ProfileKey == "general"
                && job.ProjectId == projectId
                && job.Enabled),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an invalid cron expression, when CreateAsync is called, then InvalidCronExpressionException is thrown and the job is not persisted")]
    public async Task CreateAsyncWithInvalidCronThrowsAsync()
    {
        var store = Substitute.For<IScheduledJobStore>();
        var service = NewService(store);

        // ScheduledJobService wraps CronExpression.Parse's FormatException
        // into the documented InvalidCronExpressionException so callers see
        // the typed exception matching the XML doc.
        await Should.ThrowAsync<InvalidCronExpressionException>(async () =>
            await service.CreateAsync(
                new CreateScheduledJobCommand(
                    ProjectId: new ProjectId(Guid.CreateVersion7()),
                    CronExpression: "this is not a cron",
                    ProfileKey: "general",
                    BriefJson: "{}",
                    RunOnOnceAt: null,
                    Enabled: true),
                TestContext.Current.CancellationToken));

        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown job id, when GetAsync is called, then ScheduledJobNotFoundException is thrown")]
    public async Task GetAsyncWithUnknownIdThrowsAsync()
    {
        var store = Substitute.For<IScheduledJobStore>();
        store.FindAsync(Arg.Any<ScheduledJobId>(), Arg.Any<CancellationToken>())
            .Returns((ScheduledJob?)null);
        var service = NewService(store);
        var unknown = Guid.CreateVersion7();

        await Should.ThrowAsync<ScheduledJobNotFoundException>(async () =>
            await service.GetAsync(unknown, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an existing job, when UpdateAsync is called, then the job is patched and persisted")]
    public async Task UpdateAsyncPatchesExistingJobAsync()
    {
        var store = Substitute.For<IScheduledJobStore>();
        var job = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
            "{}",
            runOnOnceAt: null,
            enabled: true,
            now: anchorTime);
        store.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        var service = NewService(store);

        var view = await service.UpdateAsync(
            new UpdateScheduledJobCommand(
                JobId: job.Id,
                CronExpression: "0 9 * * *",
                ProfileKey: "ops",
                Enabled: false),
            TestContext.Current.CancellationToken);

        view.CronExpression.ShouldBe("0 9 * * *");
        view.ProfileKey.ShouldBe("ops");
        view.Enabled.ShouldBeFalse();
        await store.Received(1).UpdateAsync(
            Arg.Is<ScheduledJob>(persisted => persisted.Id == job.Id
                && persisted.CronExpression == "0 9 * * *"
                && persisted.ProfileKey == "ops"
                && !persisted.Enabled),
            TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unknown id, when DeleteAsync is called, then the store is asked to delete it (idempotent — missing is a no-op)")]
    public async Task DeleteAsyncForwardsToStoreAsync()
    {
        var store = Substitute.For<IScheduledJobStore>();
        var service = NewService(store);
        var unknown = Guid.CreateVersion7();

        await service.DeleteAsync(unknown, TestContext.Current.CancellationToken);

        await store.Received(1).DeleteAsync(
            Arg.Is<ScheduledJobId>(id => id.Value == unknown),
            TestContext.Current.CancellationToken);
    }

    private static ScheduledJobService NewService(IScheduledJobStore store)
    {
        return new ScheduledJobService(
            store,
            new FixedClock(anchorTime),
            new CreateScheduledJobValidator(),
            new UpdateScheduledJobValidator(),
            NullLogger<ScheduledJobService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
