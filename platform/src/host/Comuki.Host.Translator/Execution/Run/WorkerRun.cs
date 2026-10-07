using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Runtime;

namespace Comuki.Host.Translator.Execution.Run;

/// <summary>
/// State of one claimed work item: the claim, the open gRPC session
/// to the host, and the cancellation that a Stop / LeaseExpired
/// command (or process shutdown) trips to kill the harness session.
/// The <see cref="HarnessSession"/> is set by the <c>PiPump</c>
/// before the events iterator starts; the
/// <c>WorkerCommandHandler</c> reads it when a
/// <see cref="Shared.Contracts.Grpc.TurnInput"/> command
/// arrives mid-cycle and writes the operator's turn into the
/// harness's stdin writer.
/// </summary>
/// <param name="claimed"></param>
/// <param name="session"></param>
public sealed class WorkerRun(
    ClaimedWorkItemResponse claimed,
    WorkerSession session) : IAsyncDisposable
{
    /// <summary>
    /// boundary: linked to the hosted-service stop; cancelled by Stop/LeaseExpired commands
    /// </summary>
    public required CancellationTokenSource RunCancellation { get; init; }

    /// <summary>
    /// Wall-clock instant the current cycle spawned the harness
    /// (harden-worker-runtime Phase 1, design D2). The
    /// <c>DeadlinePolicy</c> reads this to compute
    /// <c>turn_elapsed_ms</c> against <c>TurnBudget</c>. Set by
    /// <see cref="Loop.TranslatorLoop"/> right before
    /// <see cref="Loop.PiPump.PumpAsync"/>.
    /// </summary>
    public required DateTimeOffset RunStartedAt { get; init; }

    /// <summary>
    /// Wall-clock instant the worker process itself started
    /// (harden-worker-runtime Phase 1, design D2). The
    /// <c>DeadlinePolicy</c> reads this to compute
    /// <c>run_elapsed_ms</c> against <c>RunBudget</c>. The
    /// <see cref="TranslatorHostedService"/> seeds this once at
    /// startup; the same value is reused across every cycle inside
    /// the process.
    /// </summary>
    public required DateTimeOffset ProcessStartedAt { get; init; }

    /// <summary>
    /// The cloned repository root (harden-pi-worker-sandbox 4.3) — the
    /// working directory the harness, exec commands and restore opcodes run in.
    /// Falls back to the configured working directory when set to
    /// <c>null</c>.
    /// </summary>
    public string? RepositoryDirectory { get; init; }

    /// <summary>
    /// The live harness session (production: <c>pi --mode rpc</c>;
    /// tests: <c>TestFakeHarness</c> echo). The <c>PiPump</c>
    /// sets this before the events iterator starts; the
    /// <c>WorkerCommandHandler</c> reads it to write inbound
    /// <see cref="Shared.Contracts.Grpc.TurnInput"/>
    /// commands. <c>null</c> on construction — the pump assigns
    /// it during the first <c>await</c> of
    /// <c>IHarnessRuntime.StartSessionAsync</c>.
    /// </summary>
    public IHarnessSession? HarnessSession { get; set; }

    /// <summary>The claimed item this run executes.</summary>
    public ClaimedWorkItemResponse Claimed => claimed;

    /// <summary>The worker bidi stream the run reports over.</summary>
    public WorkerSession Session => session;

    /// <summary>Set when the orchestrator said the lease expired — completion must be skipped.</summary>
    public bool LeaseLost { get; set; }

    /// <summary>Set when the orchestrator sent a Stop command.</summary>
    public bool StopRequested { get; set; }

    /// <summary>
    /// True after the harness has emitted an
    /// <see cref="Parsing.PiEvent.AgentSettledEvent"/>.
    /// The <c>WorkerCommandHandler</c> reads this to choose between
    /// <c>steer</c> (mid-flight) and <c>follow_up</c> (post-settled)
    /// on the harness's stdin. Reset to <c>false</c> on every
    /// <see cref="Parsing.PiEvent.AgentStartEvent"/>
    /// so a follow-up cycle that hasn't settled yet still gets
    /// <c>steer</c>.
    /// </summary>
    public bool HasAgentSettled { get; set; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        RunCancellation.Dispose();
        return session.DisposeAsync();
    }
}
