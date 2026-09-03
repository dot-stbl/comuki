using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Costs;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Costs;

/// <summary>
/// The host budget gate writes <c>Cancelled</c> + appends
/// <see cref="RunEventTypes.BudgetExceeded"/> through the real
/// OrchestrationDbContext + IRunJournal. The four truth-table scenarios are:
/// happy-path cancellation of a non-terminal run, a no-op on a missing run,
/// a no-op on a terminal run, and a payload carrying the spent/limit
/// deltas the gate is supposed to surface.
/// </summary>
public sealed class OrchestrationBudgetGateShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private IServiceProvider services = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await container.StartAsync(cancellationToken);
        var connectionString = container.GetConnectionString();

        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using (var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options))
        {
            await orchestrationDb.Database.MigrateAsync(cancellationToken);
        }

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(identityOptions, connectionString);
        await using (var identityDb = new IdentityDbContext(identityOptions.Options))
        {
            await identityDb.Database.MigrateAsync(cancellationToken);
        }

        var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
        await using (var projectsDb = new ProjectsDbContext(projectsOptions.Options))
        {
            await projectsDb.Database.MigrateAsync(cancellationToken);
        }

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddOrchestrationPersistence(connectionString);
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddScoped<IRunJournal, Engine.Orchestration.Infrastructure.Journal.RunJournalEf>();
        _ = services.AddScoped<OrchestrationBudgetGate>();
        this.services = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (services is ServiceProvider provider)
        {
            await provider.DisposeAsync();
        }

        await container.DisposeAsync();
    }

    private async Task<Run> SeedRunAsync(RunStatus targetStatus)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(ProjectId.New(), now);

        // Drive through the legal transition chain so the aggregate guard
        // accepts the target status — Queued is the only legal entry and
        // Succeeded has to come through Running first.
        if (targetStatus is RunStatus.Running or RunStatus.Succeeded or RunStatus.Cancelled)
        {
            run.TransitionTo(RunStatus.Running, now);
        }

        if (targetStatus is RunStatus.Succeeded)
        {
            run.TransitionTo(RunStatus.Succeeded, now);
        }

        if (targetStatus is RunStatus.Cancelled)
        {
            run.TransitionTo(RunStatus.Cancelled, now);
        }

        db.Runs.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    private async Task<(RunStatus RunStatus, string? Type, string? Payload, long? Spent, long? Limit)> ReadGateResultAsync(RunId runId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var journal = scope.ServiceProvider.GetRequiredService<IRunJournal>();

        var run = await db.Runs.FirstOrDefaultAsync(candidate => candidate.Id == runId, TestContext.Current.CancellationToken);
        var timeline = await journal.ReadTimelineAsync(runId, 1, 16, TestContext.Current.CancellationToken);
        var budget = timeline.LastOrDefault(static entry => entry.Type == RunEventTypes.BudgetExceeded);

        if (budget is null)
        {
            return (run?.Status ?? RunStatus.Queued, null, null, null, null);
        }

        var document = JsonDocument.Parse(budget.PayloadJson);
        var root = document.RootElement;
        var spent = root.TryGetProperty("spentUsdMicros", out var spentElement) ? spentElement.GetInt64() : 0;
        var limit = root.TryGetProperty("hardLimitUsdMicros", out var limitElement) ? limitElement.GetInt64() : 0;

        return (run!.Status, budget.Type, budget.PayloadJson, spent, limit);
    }

    [Fact(DisplayName = "Given a running run, when the budget gate hard-stops, then the run is cancelled and a BudgetExceeded journal row is appended")]
    public async Task HardStopRunningRunAsync()
    {
        var run = await SeedRunAsync(RunStatus.Running);

        await using var scope = services.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<OrchestrationBudgetGate>();
        const long spent = 250_000_000L;
        const long limit = 200_000_000L;

        await gate.HardStopAsync(run.Id, run.ProjectId, spent, limit, TestContext.Current.CancellationToken);

        var result = await ReadGateResultAsync(run.Id);
        result.RunStatus.ShouldBe(RunStatus.Cancelled);
        result.Type.ShouldBe(RunEventTypes.BudgetExceeded);
        result.Spent.ShouldBe(spent);
        result.Limit.ShouldBe(limit);
    }

    [Fact(DisplayName = "Given a queued run, when the budget gate hard-stops, then the run is cancelled and the journal payload carries from=Queued")]
    public async Task HardStopQueuedRunAsync()
    {
        var run = await SeedRunAsync(RunStatus.Queued);

        await using var scope = services.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<OrchestrationBudgetGate>();
        await gate.HardStopAsync(run.Id, run.ProjectId, 1_000L, 500L, TestContext.Current.CancellationToken);

        var result = await ReadGateResultAsync(run.Id);
        result.RunStatus.ShouldBe(RunStatus.Cancelled);
        result.Payload.ShouldNotBeNull();
        using var document = JsonDocument.Parse(result.Payload);
        document.RootElement.GetProperty("from").GetString().ShouldBe("Queued");
        document.RootElement.GetProperty("to").GetString().ShouldBe("Cancelled");
        document.RootElement.GetProperty("spentUsdMicros").GetInt64().ShouldBe(1_000L);
        document.RootElement.GetProperty("hardLimitUsdMicros").GetInt64().ShouldBe(500L);
    }

    [Fact(DisplayName = "Given an unknown run id, when the budget gate hard-stops, then nothing is mutated and no journal row is appended")]
    public async Task HardStopMissingRunIsNoOpAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<OrchestrationBudgetGate>();

        await gate.HardStopAsync(RunId.New(), ProjectId.New(), 1L, 1L, TestContext.Current.CancellationToken);

        await using var verifyScope = services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var journal = verifyScope.ServiceProvider.GetRequiredService<IRunJournal>();
        var any = await db.RunEvents.CountAsync(static entry => entry.Type == RunEventTypes.BudgetExceeded, TestContext.Current.CancellationToken);
        any.ShouldBe(0);
    }

    [Fact(DisplayName = "Given an already terminal run, when the budget gate hard-stops, then no journal row is appended and the status is preserved")]
    public async Task HardStopTerminalRunIsNoOpAsync()
    {
        var run = await SeedRunAsync(RunStatus.Succeeded);

        await using var scope = services.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<OrchestrationBudgetGate>();
        await gate.HardStopAsync(run.Id, run.ProjectId, 1L, 1L, TestContext.Current.CancellationToken);

        var result = await ReadGateResultAsync(run.Id);
        result.RunStatus.ShouldBe(RunStatus.Succeeded);
        result.Type.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a run that is already cancelled, when the budget gate hard-stops again, then no second journal row is appended")]
    public async Task HardStopCancelledRunIsNoOpAsync()
    {
        var run = await SeedRunAsync(RunStatus.Cancelled);

        await using var scope = services.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<OrchestrationBudgetGate>();
        await gate.HardStopAsync(run.Id, run.ProjectId, 1L, 1L, TestContext.Current.CancellationToken);

        var result = await ReadGateResultAsync(run.Id);
        result.RunStatus.ShouldBe(RunStatus.Cancelled);
        result.Type.ShouldBeNull();
    }
}
