using System.Text.Json;
using Comuki.Host.Translator.Api.Contracts;
using Comuki.Host.Translator.Api.Models.Requests;
using Comuki.Host.Translator.Execution.Clone;
using Comuki.Host.Translator.Execution.Commands;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Host.Translator.Execution.Restore;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Profiles;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Contracts.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>
/// One claim-execute-report cycle (T3.3): claim an item over REST, prepare
/// profiles, clone the product repository (harden-pi-worker-sandbox 4.3),
/// run the after-clone restore step (add-worker-environments 4.3),
/// open the worker gRPC stream, run pi under heartbeat + command
/// handling, report the outcome over the stream, then complete/fail the
/// item — unless the lease was lost, in which case ownership is gone and
/// nothing is written. Returns false when the queue had nothing for this
/// worker.
/// </summary>
/// <remarks>
/// Stage conditions (harden-pi-worker-sandbox 5.1, spec D6) are
/// journaled as <c>worker.condition</c> entries at three checkpoints:
/// <c>WorkspacePrepared</c> after profiles + restore succeed,
/// <c>EgressApplied</c> once the claim body has been received (the
/// orchestrator returned a claim = the compute provider has fenced the
/// slot), and <c>AgentRunning</c> only after the pi process has
/// actually started. Each is a best-effort journal hint: a failed send
/// is logged, not propagated — the work item's lease / completion path
/// stays unaffected.
/// </remarks>
/// <param name="api"></param>
/// <param name="harness">The runtime half of the harness SPI (replaces the v1.x <c>IPiRunner</c>); the <c>PiPump</c> opens the live session through it.</param>
/// <param name="workerService"></param>
/// <param name="profilesProvider"></param>
/// <param name="sourceCloneRunner"></param>
/// <param name="restoreRunner"></param>
/// <param name="heartbeat"></param>
/// <param name="debugExecHost">Opt-in operator debug exec surface (harden-pi-worker-sandbox 5.3).</param>
/// <param name="options"></param>
/// <param name="clock"></param>
/// <param name="loggerFactory"></param>
/// <param name="logger"></param>
public sealed class TranslatorLoop(
    IOrchestratorApi api,
    IHarnessRuntime harness,
    IWorkerService workerService,
    IProfilesProvider profilesProvider,
    SourceCloneRunner sourceCloneRunner,
    RestoreRunner restoreRunner,
    HeartbeatMonitor heartbeat,
    IDebugExecHost debugExecHost,
    IOptions<TranslatorOptions> options,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<TranslatorLoop> logger)
{
    /// <summary>Attempts one full work item cycle. False = queue empty.</summary>
    /// <param name="stoppingToken"></param>
    public async Task<bool> TryRunOnceAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var claim = await api.ClaimAsync(
            new ClaimWorkItemRequest(opts.WorkerImage, opts.ProfilesRef, opts.ProfileKey, opts.EnvClass),
            stoppingToken);
        if (claim.Content is not { } claimed)
        {
            return false;
        }

        logger.LogInformation(
            "Claimed work item {WorkItemId} of run {RunId} (attempt {Attempt})",
            claimed.WorkItemId,
            claimed.RunId,
            claimed.Attempt);

        await profilesProvider.PrepareAsync(opts.ProfilesRef, stoppingToken);

        // Workspace source clone (harden-pi-worker-sandbox 4.3): the
        // claim's SourceGitUrl cloned into <wd>/source BEFORE restore and
        // pi. A missing URL or a failed clone (private repo without a
        // credential included) fails the item via REST — pi is not
        // started and no gRPC session is opened. The resolved credential
        // is consumed by the clone child process only and deleted with
        // its throwaway git config afterwards; it never reaches pi env.
        var cloneOutcome = await sourceCloneRunner.RunAsync(
            opts.WorkingDirectory,
            claimed.SourceGitUrl,
            claimed.SourceGitRef,
            claimed.GitCredential,
            stoppingToken);
        if (cloneOutcome.Kind is not SourceCloneOutcomeKind.Succeeded)
        {
            var cloneFailResponse = await api.FailAsync(
                claimed.WorkItemId,
                new FailWorkItemRequest(cloneOutcome.Reason, claimed.Generation),
                stoppingToken);
            if (!cloneFailResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Fail of work item {WorkItemId} (clone outcome {Kind}) rejected with {StatusCode}",
                    claimed.WorkItemId,
                    cloneOutcome.Kind,
                    cloneFailResponse.StatusCode);
            }

            return true;
        }

        // After-clone restore (add-worker-environments 4.3, spec scenario
        // "Restore opcodes run on the slot after clone") — runs against
        // the freshly cloned repository root. On any failure-kind
        // outcome the loop fails the item via REST and moves on — pi is
        // not started and no gRPC session is opened.
        var restoreOutcome = await restoreRunner.RunAsync(
            cloneOutcome.RepositoryDirectory,
            claimed.EnvClass,
            EnvironmentClassCatalog.OpcodesFor(claimed.EnvClass),
            stoppingToken);
        if (restoreOutcome.Kind is not RestoreOutcomeKind.Succeeded and not RestoreOutcomeKind.Skipped)
        {
            var failResponse = await api.FailAsync(
                claimed.WorkItemId,
                new FailWorkItemRequest(restoreOutcome.Reason, claimed.Generation),
                stoppingToken);
            if (!failResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Fail of work item {WorkItemId} (restore outcome {Kind}) rejected with {StatusCode}",
                    claimed.WorkItemId,
                    restoreOutcome.Kind,
                    failResponse.StatusCode);
            }

            return true;
        }

        await using var run = new WorkerRun(
            claimed,
            WorkerSession.Open(workerService, opts.WorkerToken, stoppingToken))
        {
            RunCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken),
            RepositoryDirectory = cloneOutcome.RepositoryDirectory,
        };

        await run.Session.SendAsync(WorkerEventEnvelope.ToStartEvent(claimed), stoppingToken);

        // WorkspacePrepared — profiles + restore both succeeded; the
        // claim has been received and the slot has a working tree.
        await SendConditionAsync(run, WorkspacePreparedCondition, value: true, stoppingToken);

        // EgressApplied — the claim body is the orchestrator's confirmation
        // that the compute provider fenced the slot before scaling. The
        // Translator's contract here is to surface what the claim told it;
        // the actual fence lives on the host side (slice 2).
        await SendConditionAsync(run, EgressAppliedCondition, value: true, stoppingToken);

        var commandTask = new WorkerCommandHandler(
            run,
            cloneOutcome.RepositoryDirectory,
            options,
            debugExecHost,
            loggerFactory.CreateLogger<WorkerCommandHandler>())
            .ConsumeAsync(stoppingToken);
        var heartbeatTask = heartbeat.RunAsync(
            claimed.WorkItemId, claimed.Generation, opts.HeartbeatInterval, run.RunCancellation.Token, stoppingToken);

        var summary = new WorkerRunSummary();
        var startedAt = clock.GetUtcNow();
        var outcome = await PiPump.PumpAsync(
            harness, run, summary, startedAt, clock, loggerFactory.CreateLogger(nameof(PiPump)));

        // AgentRunning — pi was started and the pump returned (success or
        // otherwise). The pump does not distinguish "started and crashed"
        // from "started and ran"; the report event carries the bottom line.
        await SendConditionAsync(run, AgentRunningCondition, value: true, CancellationToken.None);

        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToReportEvent(claimed.WorkItemId, outcome),
                CancellationToken.None);
        }
        catch (RpcException exception)
        {
            logger.LogWarning(exception, "Report of work item {WorkItemId} could not be delivered", claimed.WorkItemId);
        }

        run.RunCancellation.Cancel();
        await run.Session.CloseAsync();
        await commandTask;
        var leaseHeld = await heartbeatTask;

        if (leaseHeld is not true || run.LeaseLost)
        {
            logger.LogWarning(
                "Lease of work item {WorkItemId} lost — skipping completion, the reaper owns the item",
                claimed.WorkItemId);
            return true;
        }

        // Drain (harden-pi-worker-sandbox 5.2, spec D7) — ship the
        // accumulated artifact list to the host right before complete /
        // fail. The packager reads the journal's worker.drained entry
        // and skips any prefix it has already bundled; a drain send
        // failure is logged and never blocks the complete / fail
        // (the artifact list is a hint, not a gate).
        await SendDrainAsync(run, stoppingToken);

        if (outcome.Status == PiOutcome.SuccessStatus)
        {
            var reportJson = JsonSerializer.Serialize(
                WorkerEventEnvelope.ToReportEvent(claimed.WorkItemId, outcome).Report,
                JsonSerializerOptions.Web);
            var completed = await api.CompleteAsync(
                claimed.WorkItemId, new CompleteWorkItemRequest(reportJson, claimed.Generation), stoppingToken);
            if (!completed.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Complete of work item {WorkItemId} rejected with {StatusCode}",
                    claimed.WorkItemId,
                    completed.StatusCode);
            }

            return true;
        }

        var failureReason = outcome.ErrorText.Length > 0
            ? $"{outcome.Status}: {outcome.ErrorText}"
            : $"status {outcome.Status}";
        var failed = await api.FailAsync(
            claimed.WorkItemId, new FailWorkItemRequest(failureReason, claimed.Generation), stoppingToken);
        if (!failed.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Fail of work item {WorkItemId} rejected with {StatusCode}",
                claimed.WorkItemId,
                failed.StatusCode);
        }

        return true;
    }

    /// <summary>
    /// Drains the run-scoped <see cref="ArtifactAccumulator"/> as a single
    /// <c>worker.drained</c> event (harden-pi-worker-sandbox 5.2, spec
    /// D7). Best-effort: a transport failure is logged at warning and
    /// never propagated — the work item's complete/fail path stays
    /// unaffected (the drain is a journal hint, not a gate, per the
    /// packager's <c>IsBundledAsync</c> idempotence contract).
    /// </summary>
    /// <param name="run">The gRPC-bound worker run.</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    private async Task SendDrainAsync(WorkerRun run, CancellationToken cancellationToken)
    {
        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToDrainEvent(run.Claimed.WorkItemId, run.ArtifactAccumulator.Snapshot()),
                cancellationToken);
        }
        catch (RpcException exception)
        {
            logger.LogWarning(
                exception,
                "Drain of work item {WorkItemId} could not be delivered",
                run.Claimed.WorkItemId);
        }
    }

    /// <summary>
    /// Sends a boolean condition event over the run's gRPC stream.
    /// Best-effort: a transport failure is logged at warning and never
    /// propagated — the work item's lease / completion path stays
    /// unaffected (the condition is a journal hint, not a gate).
    /// </summary>
    /// <param name="run">The gRPC-bound worker run.</param>
    /// <param name="name">Sandbox condition name (<c>WorkspacePrepared</c>, etc.).</param>
    /// <param name="value">Boolean state the condition flipped to.</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    private async Task SendConditionAsync(
        WorkerRun run,
        string name,
        bool value,
        CancellationToken cancellationToken)
    {
        try
        {
            await run.Session.SendAsync(
                WorkerEventEnvelope.ToConditionEvent(run.Claimed.WorkItemId, name, value),
                cancellationToken);
        }
        catch (RpcException exception)
        {
            logger.LogWarning(
                exception,
                "Condition {Name}={Value} for work item {WorkItemId} could not be delivered",
                name,
                value,
                run.Claimed.WorkItemId);
        }
    }

    /// <summary>Sandbox condition name — clone + restore finished.</summary>
    private const string WorkspacePreparedCondition = "WorkspacePrepared";

    /// <summary>Sandbox condition name — compute provider fenced the slot.</summary>
    private const string EgressAppliedCondition = "EgressApplied";

    /// <summary>Sandbox condition name — pi process has started.</summary>
    private const string AgentRunningCondition = "AgentRunning";
}
