using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Execution;

/// <summary>
/// The seam <see cref="ScenarioRunner"/> drives a scenario through — "seeds
/// via REST", "provisions compute", "asserts" in design.md's architecture
/// diagram. <c>Comuki.EndToEnd.AgentLoop</c> (a separate test project)
/// implements this against the real <c>HostComposer.ComposeAsync</c>
/// composition + the real
/// <see cref="Engine.Compute.Providers.DockerComputeProvider"/> for
/// T2a; a future T2b/WS7 harness implements the same interface with a real
/// <c>pi</c> pointed at the fake-model server instead of TestFakePi —
/// <see cref="ScenarioRunner"/> itself does not change.
/// </summary>
public interface IAgentLoopHarness
{
    /// <summary>
    /// Seeds the scenario's ticket through the real intake webhook endpoint
    /// (admission rule + source connection already provisioned by the
    /// harness's own setup) and returns the run/work-item the real
    /// <c>IntakeRunLauncher</c> created.
    /// </summary>
    /// <param name="scenario"></param>
    /// <param name="cancellationToken"></param>
    public Task<SeededWorkItem> SeedTicketAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions the real worker container for <paramref name="scenario"/>'s
    /// claim labels. Throws (uncaught by this method — <see cref="ScenarioRunner"/>
    /// converts it into a named <c>compute.start</c> failure) when the
    /// compute provider itself cannot start the container (e.g. an image tag
    /// that was never built).
    /// </summary>
    /// <param name="scenario"></param>
    /// <param name="cancellationToken"></param>
    public Task<WorkerHandle> StartWorkerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default);

    /// <summary>Reads the run's timeline, oldest first — the same read <see cref="Journal.JournalConditionEvaluator"/> evaluates conditions against.</summary>
    /// <param name="runId"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Reads the work item's current status string (matches <c>WorkItemStatus</c>'s member names — Queued/Running/Succeeded/Failed).</summary>
    /// <param name="workItemId"></param>
    /// <param name="cancellationToken"></param>
    public Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken = default);

    /// <summary>Stops and removes the worker container. Called from the runner's own cleanup — must not throw on an already-stopped/never-started handle.</summary>
    /// <param name="handle"></param>
    /// <param name="cancellationToken"></param>
    public Task StopWorkerAsync(WorkerHandle handle, CancellationToken cancellationToken = default);
}

/// <summary>A ticket seeded through the real webhook — the run/work-item the real queue claim will match against.</summary>
/// <param name="RunId">The created run's id.</param>
/// <param name="WorkItemId">The created work item's id.</param>
public sealed record SeededWorkItem(Guid RunId, Guid WorkItemId);
