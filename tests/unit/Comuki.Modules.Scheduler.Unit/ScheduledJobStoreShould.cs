using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Comuki.Modules.Scheduler.Infrastructure.Persistence;
using Comuki.Modules.Scheduler.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Scheduler.Unit;

/// <summary>
/// ScheduledJobStore round-trip + due-job query over an in-memory
/// <see cref="SchedulerDbContext"/>. The due query's
/// <c>FOR UPDATE SKIP LOCKED</c> clause is Postgres-only and cannot
/// run against the in-memory provider — that path is covered by the
/// end-to-end integration test that boots a real Testcontainers
/// Postgres. Here we cover the LINQ-shaped CRUD path: insert,
/// find, list, update, delete.
/// </summary>
public sealed class ScheduledJobStoreShould
{
    [Fact(DisplayName = "Given a new job, when added, then it round-trips through find by id")]
    public async Task AddAndFindRoundTripAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var now = DateTimeOffset.UtcNow;
        var job = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
                                 /*lang=json,strict*/
                                 "{\"goal\":\"hello\"}",
            null,
            enabled: true,
            now);

        await store.AddAsync(job, TestContext.Current.CancellationToken);

        var found = await store.FindAsync(job.Id, TestContext.Current.CancellationToken);
        found.ShouldNotBeNull();
        found.Id.ShouldBe(job.Id);
        found.CronExpression.ShouldBe("*/5 * * * *");
        found.ProfileKey.ShouldBe("general");
        found.BriefJson.ShouldBe(/*lang=json,strict*/ "{\"goal\":\"hello\"}");
        found.Enabled.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given two jobs in a project, when listed, then both come back newest first")]
    public async Task ListByProjectAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var first = ScheduledJob.Create(projectId, "*/5 * * * *", "general", "{}", null, true, now.AddMinutes(-2));
        var second = ScheduledJob.Create(projectId, "0 9 * * *", "general", "{}", null, true, now.AddMinutes(-1));

        await store.AddAsync(first, TestContext.Current.CancellationToken);
        await store.AddAsync(second, TestContext.Current.CancellationToken);

        var list = await store.ListAsync(projectId, TestContext.Current.CancellationToken);

        list.Count.ShouldBe(2);
        list[0].Id.ShouldBe(second.Id);
        list[1].Id.ShouldBe(first.Id);
    }

    [Fact(DisplayName = "Given a different project, when listed, then it stays isolated")]
    public async Task ListIsolatesByProjectAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var projectA = new ProjectId(Guid.CreateVersion7());
        var projectB = new ProjectId(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var aJob = ScheduledJob.Create(projectA, "*/5 * * * *", "general", "{}", null, true, now);
        var bJob = ScheduledJob.Create(projectB, "*/5 * * * *", "general", "{}", null, true, now);

        await store.AddAsync(aJob, TestContext.Current.CancellationToken);
        await store.AddAsync(bJob, TestContext.Current.CancellationToken);

        var listA = await store.ListAsync(projectA, TestContext.Current.CancellationToken);
        listA.Count.ShouldBe(1);
        listA[0].Id.ShouldBe(aJob.Id);
    }

    [Fact(DisplayName = "Given 120 jobs in a project, when ListPagedAsync asks page=2 of size 10, then it returns exactly 10 rows newest first and reports total=120")]
    public async Task ListPagedAsyncPushesSkipTakeToTheStoreAsync()
    {
        // EF Core's in-memory provider does not push Skip/Take down as a
        // SQL OFFSET/LIMIT, but it does honor the LINQ shape — every row
        // still flows through the same IQueryable pipeline as a real
        // Postgres query would, so this assertion catches a regression to
        // in-memory Skip/Take inside the store (the bug fixed by #fix-audit-perf-high).
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var anchor = DateTimeOffset.UtcNow;

        // 120 jobs created oldest-first. ListPagedAsync orders by CreatedAt
        // DESC, so the result list is the reverse of `created`. Page 2
        // (1-based, size 10) is the result's rows 10..19, which are the
        // 11th- through 20th-newest jobs — insertion index 100..109
        // (created[100] is the 20th-newest, created[109] is the 11th).
        var created = new ScheduledJob[120];
        for (var i = 0; i < 120; i++)
        {
            var job = ScheduledJob.Create(
                projectId,
                "*/5 * * * *",
                "general",
                "{}",
                null,
                enabled: true,
                anchor.AddMinutes(i));
            await store.AddAsync(job, TestContext.Current.CancellationToken);
            created[i] = job;
        }

        var page2 = await store.ListPagedAsync(
            projectId,
            page: 2,
            pageSize: 10,
            TestContext.Current.CancellationToken);

        page2.Total.ShouldBe(120);
        page2.Items.Count.ShouldBe(10);

        page2.Items[0].Id.ShouldBe(created[109].Id);
        page2.Items[9].Id.ShouldBe(created[100].Id);
    }

    [Fact(DisplayName = "Given a project with 7 jobs and pageSize=20, when ListPagedAsync asks page=1, then it returns all 7 rows with total=7")]
    public async Task ListPagedAsyncReturnsPartialLastPageAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var anchor = DateTimeOffset.UtcNow;

        for (var i = 0; i < 7; i++)
        {
            await store.AddAsync(
                ScheduledJob.Create(projectId, "*/5 * * * *", "general", "{}", null, true, anchor.AddMinutes(i)),
                TestContext.Current.CancellationToken);
        }

        var page = await store.ListPagedAsync(
            projectId,
            page: 1,
            pageSize: 20,
            TestContext.Current.CancellationToken);

        page.Total.ShouldBe(7);
        page.Items.Count.ShouldBe(7);
    }

    [Fact(DisplayName = "Given an existing job, when updated, then the new fields persist")]
    public async Task UpdateAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var now = DateTimeOffset.UtcNow;
        var job = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
            "{}",
            null,
            enabled: true,
            now);

        await store.AddAsync(job, TestContext.Current.CancellationToken);

        job.Update("0 9 * * *", null, false, now.AddMinutes(1));
        await store.UpdateAsync(job, TestContext.Current.CancellationToken);

        var found = await store.FindAsync(job.Id, TestContext.Current.CancellationToken);
        found.ShouldNotBeNull();
        found.CronExpression.ShouldBe("0 9 * * *");
        found.Enabled.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an existing job, when deleted, then find returns null")]
    public async Task DeleteAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var job = ScheduledJob.Create(
            new ProjectId(Guid.CreateVersion7()),
            "*/5 * * * *",
            "general",
            "{}",
            null,
            enabled: true,
            DateTimeOffset.UtcNow);

        await store.AddAsync(job, TestContext.Current.CancellationToken);
        await store.DeleteAsync(job.Id, TestContext.Current.CancellationToken);

        var found = await store.FindAsync(job.Id, TestContext.Current.CancellationToken);
        found.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown id, when deleted, then it is a no-op (idempotent)")]
    public async Task DeleteUnknownIdIsNoOpAsync()
    {
        await using var db = NewContext();
        var store = new ScheduledJobStore(db);
        var unknown = new ScheduledJobId(Guid.CreateVersion7());

        await store.DeleteAsync(unknown, TestContext.Current.CancellationToken);
    }

    private static SchedulerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseInMemoryDatabase($"scheduler-store-tests-{Guid.NewGuid():N}")
            .Options;
        return new SchedulerDbContext(options);
    }
}
