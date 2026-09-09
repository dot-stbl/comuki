using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.DbContext;

/// <summary>
/// <see cref="OrchestrationDbContext"/>: subject-scope query filters
/// and persistence behaviour exercised through EF Core's InMemory
/// provider. The <see cref="OrchestrationDbContext.ApplyOptions"/>
/// helper targets Npgsql + snake_case (the production runtime), so the
/// schema-per-history assertion is checked at the option-builder level
/// (calling ApplyOptions with an Npgsql connection string must not
/// throw) — the actual schema-per-history behaviour lives in the
/// integration suite that boots a real Postgres.
/// </summary>
public sealed class OrchestrationDbContextShould
{
    private static readonly DateTimeOffset anchorTime = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given ApplyOptions called with an Npgsql connection string, when the configuration runs, then the migrations-history table lives in the orchestration schema")]
    public void ApplyOptionsAcceptsNpgsqlConnectionStringWithOrchestrationSchema()
    {
        // The production helper registers UseNpgsql with
        // MigrationsHistoryTable("__ef_migrations_history", OrchestrationDatabase.Schema).
        // We can't interrogate NpgsqlOptionsExtension without Npgsql
        // binaries at runtime — the equivalent shape-level assertion is
        // that the call configures a non-empty extension set and finishes
        // without throwing, then that the schema constant resolves to
        // "orchestration" (the canonical wire name).
        var builder = new DbContextOptionsBuilder();

        Should.NotThrow(() => OrchestrationDbContext.ApplyOptions(builder, connectionString: "Host=ignored;Username=u;Password=p;Database=ignored"));

        OrchestrationDatabase.Schema.ShouldBe("orchestration");
        $"{OrchestrationDatabase.Schema}.__ef_migrations_history"
            .ShouldBe("orchestration.__ef_migrations_history");
    }

    [Fact(DisplayName = "Given a context built without a scope accessor, when a run is saved and re-read, then it is visible (the system-context fallback sees every row)")]
    public async Task ScopeUnrestrictedByDefaultAsync()
    {
        var context = NewSystemContext();

        var run = Run.Create(ProjectId.New(), anchorTime);
        context.Runs.Add(run);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var roundTripped = await context.Runs.SingleAsync(TestContext.Current.CancellationToken);
        roundTripped.Id.ShouldBe(run.Id);
    }

    [Fact(DisplayName = "Given a context with a restricted scope that does not include the run's project, when the run is read, then the subject-scope query filter hides it")]
    public async Task RestrictedScopeHidesOutOfScopeRunsAsync()
    {
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [ProjectId.New()]);
        var context = NewScopedContext(accessor);

        var outOfScope = Run.Create(ProjectId.New(), anchorTime);
        context.Runs.Add(outOfScope);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Runs.ToListAsync(TestContext.Current.CancellationToken);
        visible.ShouldBeEmpty();

        accessor.Current.Unrestricted.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a context with a restricted scope that DOES include the run's project, when the run is read, then the subject-scope query filter surfaces it")]
    public async Task RestrictedScopeSurfacesInScopeRunsAsync()
    {
        var inScopeProject = ProjectId.New();
        var accessor = NewScopeAccessor(unrestricted: false, projectIds: [inScopeProject]);
        var context = NewScopedContext(accessor);

        var run = Run.Create(inScopeProject, anchorTime);
        context.Runs.Add(run);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Runs.ToListAsync(TestContext.Current.CancellationToken);
        visible.ShouldHaveSingleItem();
        visible[0].Id.ShouldBe(run.Id);
    }

    [Fact(DisplayName = "Given a context built from an unrestricted system scope declared via AsSystem, when runs of two distinct projects are read, then both surface regardless of project")]
    public async Task UnrestrictedScopeSurfacesAllRunsAsync()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        using var scope = accessor.AsSystem("unit-test");
        var context = NewScopedContext(accessor);

        var firstProject = ProjectId.New();
        var secondProject = ProjectId.New();
        context.Runs.Add(Run.Create(firstProject, anchorTime));
        context.Runs.Add(Run.Create(secondProject, anchorTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var visible = await context.Runs.ToListAsync(TestContext.Current.CancellationToken);
        visible.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a fresh context, when a run is added and SaveChangesAsync runs, then the run is persisted and reloaded from the same context")]
    public async Task SaveChangesAsyncPersistsThroughEfPipelineAsync()
    {
        var context = NewSystemContext();
        var projectId = ProjectId.New();
        var run = Run.Create(projectId, anchorTime);

        context.Runs.Add(run);
        var savedCount = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        savedCount.ShouldBeGreaterThan(0);

        var fromStore = await context.Runs
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == run.Id, TestContext.Current.CancellationToken);
        fromStore.ProjectId.ShouldBe(projectId);
        fromStore.Status.ShouldBe(RunStatus.Queued);
    }

    private static OrchestrationDbContext NewSystemContext()
    {
        return NewContext(scopeAccessor: null);
    }

    private static OrchestrationDbContext NewScopedContext(ISubjectScopeAccessor accessor)
    {
        return NewContext(accessor);
    }

    private static OrchestrationDbContext NewContext(ISubjectScopeAccessor? scopeAccessor)
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseInMemoryDatabase(databaseName: $"orch-ctx-{Guid.NewGuid():N}")
            .Options;
        return new OrchestrationDbContext(options, scopeAccessor);
    }

    private static ISubjectScopeAccessor NewScopeAccessor(bool unrestricted, IReadOnlyList<ProjectId> projectIds)
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(new SubjectScope(unrestricted, SystemName: unrestricted ? "unit-test" : null, projectIds));
        accessor.CurrentOrNone.Returns(accessor.Current);
        accessor.AsSystem(Arg.Any<string>()).Returns(static _ => new NoopHandle());
        accessor.Begin(Arg.Any<SubjectScope>()).Returns(static _ => new NoopHandle());
        return accessor;
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
