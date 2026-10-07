using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;
using Comuki.Shared.Contracts.Compute;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// T2a's <see cref="IAgentLoopHarness"/>: seeds through the real webhook,
/// provisions the real <see cref="Engine.Compute.Providers.DockerComputeProvider"/>
/// against Podman, and reads the real journal/work-item status —
/// everything <see cref="ScenarioRunner"/> needs, all
/// backed by <see cref="AgentLoopHost"/>.
/// </summary>
/// <param name="host"></param>
public sealed class AgentLoopHarness(AgentLoopHost host) : IAgentLoopHarness
{
    /// <summary>
    /// Stable, collision-free GitHub issue numbers per scenario name in this
    /// suite's fixture corpus — the Integrations module's duplicate-active-ticket check keys
    /// on <c>{repo}#{number}</c>, so two scenarios must never share one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> issueNumbersByScenario = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["add-null-check"] = 9001,
        ["bad-image-label"] = 9002,
    };

    /// <inheritdoc />
    public async Task<SeededWorkItem> SeedTicketAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        return !issueNumbersByScenario.TryGetValue(scenario.Name, out var issueNumber)
            ? throw new ScenarioValidationException(
                $"scenario '{scenario.Name}' has no assigned fixture issue number — add one to "
                    + $"{nameof(AgentLoopHarness)}.{nameof(issueNumbersByScenario)}")
            : await host.SeedTicketAsync(
                scenario.Ticket.Title, scenario.Ticket.Body, scenario.Ticket.Labels, issueNumber, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkerHandle> StartWorkerAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId.New();
        var token = host.ResolveTokenIssuer().Issue(workerId);
        var containerReachableBase = host.ContainerReachableBaseUri();
        // The dedicated HTTP/2-only listener for the worker gRPC bidi
        // stream (issue #152) — sharing the REST address with the gRPC
        // stream caused Kestrel to silently fall back to HTTP/1.1, so
        // every worker.reported journal entry was lost.
        var containerReachableGrpc = host.ContainerReachableGrpcUri();

        var request = new ComputeStartRequest
        {
            // Unused by the Translator (no COMUKI_* env mapping reads
            // COMUKI_PROJECT_ID today) — a container label only. Does not
            // need to match the webhook-seeded run's project.
            ProjectId = ProjectId.New(),
            PreIssuedWorkerId = workerId,
            ProfileKey = scenario.Worker.ProfileKey,
            ProfilesGitRef = scenario.Worker.ProfilesRef,
            EnvClass = "net10-sdk-bun",
            Image = scenario.Worker.Image,
            WorkerToken = token,
            OrchestratorGrpcUrl = containerReachableGrpc,
            // DockerComputeMapping.BuildEnvironment never sets
            // COMUKI_ORCH_HTTP itself (only COMUKI_ORCH_GRPC) — the REST
            // claim/heartbeat/complete/fail surface needs it too, so it
            // rides the caller-supplied Env extras.
            //
            // COMUKI_WORKING_DIRECTORY is intentionally not stamped here:
            // the in-memory snapshot in Comuki.Host.Translator/Program.cs
            // filters out unset env entries, so the option keeps its
            // Directory.GetCurrentDirectory() default and the container
            // mirrors the production image's documented behaviour — see
            // OpenSpec change `fix-null-translator-working-directory`.
            Env = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["COMUKI_ORCH_HTTP"] = containerReachableBase.ToString().TrimEnd('/'),
            },
        };

        return await host.ComputeProvider.StartAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return host.ReadTimelineAsync(runId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> ReadWorkItemStatusAsync(Guid workItemId, CancellationToken cancellationToken = default)
    {
        return host.ReadWorkItemStatusAsync(workItemId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopWorkerAsync(WorkerHandle handle, CancellationToken cancellationToken = default)
    {
        await host.ComputeProvider.StopAsync(handle.Id, ComputeStopReason.Draining, cancellationToken);
    }
}
