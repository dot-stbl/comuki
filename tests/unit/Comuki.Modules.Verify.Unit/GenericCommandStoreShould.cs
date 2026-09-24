using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Modules.Verify.Infrastructure.Persistence;
using Comuki.Modules.Verify.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// GenericCommandStore round-trip over an in-memory
/// <see cref="VerifyDbContext"/>. The claim query's <c>FOR UPDATE SKIP
/// LOCKED</c> clause is Postgres-only and cannot run against the
/// in-memory provider — that path is covered by
/// <c>Comuki.Modules.Verify.Integration.Migrations</c> against a real
/// Testcontainers Postgres. Here we cover the LINQ-shaped CRUD path:
/// insert, find, list, update — including that <see cref="GenericCommandRun.Arguments"/>
/// round-trips through the jsonb converter.
/// </summary>
public sealed class GenericCommandStoreShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-23T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given a new run, when added, then it round-trips through find by id including its arguments")]
    public async Task AddAndFindRoundTripAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);
        var run = GenericCommandRun.Create(
            new ProjectId(Guid.CreateVersion7()),
            "general",
            "dotnet",
            ["build", "comuki.slnx", "-c", "Debug"],
            expectedExitCode: 0,
            anchorTime);

        await store.AddAsync(run, TestContext.Current.CancellationToken);

        var found = await store.FindAsync(run.Id, TestContext.Current.CancellationToken);
        found.ShouldNotBeNull();
        found.Id.ShouldBe(run.Id);
        found.Executable.ShouldBe("dotnet");
        found.Arguments.ShouldBe(["build", "comuki.slnx", "-c", "Debug"]);
        found.Status.ShouldBe(GenericCommandStatus.Pending);
    }

    [Fact(DisplayName = "Given two runs in a project, when listed, then both come back newest first")]
    public async Task ListByProjectAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);
        var projectId = new ProjectId(Guid.CreateVersion7());
        var first = GenericCommandRun.Create(projectId, "general", "dotnet", ["--version"], 0, anchorTime.AddMinutes(-2));
        var second = GenericCommandRun.Create(projectId, "general", "dotnet", ["build"], 0, anchorTime.AddMinutes(-1));

        await store.AddAsync(first, TestContext.Current.CancellationToken);
        await store.AddAsync(second, TestContext.Current.CancellationToken);

        var list = await store.ListAsync(projectId, limit: 10, TestContext.Current.CancellationToken);

        list.Count.ShouldBe(2);
        list[0].Id.ShouldBe(second.Id);
        list[1].Id.ShouldBe(first.Id);
    }

    [Fact(DisplayName = "Given a different project, when listed, then it stays isolated")]
    public async Task ListIsolatesByProjectAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);
        var projectA = new ProjectId(Guid.CreateVersion7());
        var projectB = new ProjectId(Guid.CreateVersion7());
        var aRun = GenericCommandRun.Create(projectA, "general", "dotnet", ["--version"], 0, anchorTime);
        var bRun = GenericCommandRun.Create(projectB, "general", "dotnet", ["--version"], 0, anchorTime);

        await store.AddAsync(aRun, TestContext.Current.CancellationToken);
        await store.AddAsync(bRun, TestContext.Current.CancellationToken);

        var listA = await store.ListAsync(projectA, limit: 10, TestContext.Current.CancellationToken);
        listA.Count.ShouldBe(1);
        listA[0].Id.ShouldBe(aRun.Id);
    }

    [Fact(DisplayName = "Given a run limit, when listed, then it caps at the given limit")]
    public async Task ListRespectsLimitAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);
        var projectId = new ProjectId(Guid.CreateVersion7());
        for (var i = 0; i < 5; i++)
        {
            await store.AddAsync(
                GenericCommandRun.Create(projectId, "general", "dotnet", ["--version"], 0, anchorTime.AddSeconds(i)),
                TestContext.Current.CancellationToken);
        }

        var list = await store.ListAsync(projectId, limit: 2, TestContext.Current.CancellationToken);

        list.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given an existing run, when updated, then the new status and log persist")]
    public async Task UpdateAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        await store.AddAsync(run, TestContext.Current.CancellationToken);

        run.MarkRunning(anchorTime.AddSeconds(1));
        run.MarkCompleted(0, "[out] 10.0.303\n", anchorTime.AddSeconds(2));
        await store.UpdateAsync(run, TestContext.Current.CancellationToken);

        var found = await store.FindAsync(run.Id, TestContext.Current.CancellationToken);
        found.ShouldNotBeNull();
        found.Status.ShouldBe(GenericCommandStatus.Green);
        found.OutputLog.ShouldBe("[out] 10.0.303\n");
    }

    [Fact(DisplayName = "Given an unknown id, when found, then it returns null")]
    public async Task FindUnknownIdReturnsNullAsync()
    {
        await using var db = NewContext();
        var store = new GenericCommandStore(db);

        var found = await store.FindAsync(new GenericCommandRunId(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        found.ShouldBeNull();
    }

    private static VerifyDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VerifyDbContext>()
            .UseInMemoryDatabase($"verify-store-tests-{Guid.NewGuid():N}")
            .Options;
        return new VerifyDbContext(options);
    }
}
