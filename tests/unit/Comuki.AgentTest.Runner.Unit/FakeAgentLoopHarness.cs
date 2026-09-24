using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Reporting.Report;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// In-file test double for <see cref="IAgentLoopHarness"/> — just enough
/// behaviour to drive <see cref="ScenarioRunner"/>'s post-run cost /
/// budget checks without a real Podman / orchestrator host. Each
/// scenario sets its own <see cref="CostToReturn"/>; the runner reads it
/// via <see cref="ReadCostAsync"/>.
/// </summary>
/// <remarks>
/// This project doesn't reference NSubstitute (the brief's per-csproj
/// "either add a small manual fake OR add an NSubstitute package
/// reference — your call") — a 30-line hand-written fake is the smaller
/// change.
/// </remarks>
internal sealed class FakeAgentLoopHarness(string workItemStatus = "Succeeded", params RunEventEntry[] entries) : IAgentLoopHarness
{
    private readonly List<RunEventEntry> timeline = [.. entries];
    private readonly string workItemStatus = workItemStatus;

    /// <summary>The cost <see cref="ReadCostAsync"/> will report for the seeded work item.</summary>
    public RunCost CostToReturn { get; set; } = new();

    public Task<SeededWorkItem> SeedTicketAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SeededWorkItem(Guid.NewGuid(), Guid.NewGuid()));
    }

    public Task<WorkerHandle> StartWorkerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WorkerHandle(new WorkerId(Guid.NewGuid()), "fake"));
    }

    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RunEventEntry>>(timeline);
    }

    public Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(workItemStatus);
    }

    public Task StopWorkerAsync(WorkerHandle handle, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<RunCost> ReadCostAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CostToReturn);
    }
}
