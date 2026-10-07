namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// SPI for a per-work-item verification gate (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "Gate-provider registry is an
/// open SPI"; <c>design.md</c> §D5). The host iterates every registered
/// provider when a work item enters a terminal phase and writes a
/// <see cref="GateVerdictResult"/> per (work item, gate) pair. The
/// set is genuinely open — operator code registers additional gates
/// through <c>AddVerificationGateProvider&lt;T&gt;</c> in the host
/// composition root without touching platform code.
/// </summary>
public interface IVerificationGateProvider
{
    /// <summary>
    /// Stable dotted gate name — the row key on
    /// <c>VerificationRecord</c>, the value stamped on
    /// <see cref="GateVerdictResult.Evaluator"/>, and the literal the
    /// platform-shipped first provider uses
    /// (<c>"verify:generic-command-run"</c>). Two providers MUST NOT
    /// declare the same gate name; the host throws at composition
    /// when it sees a duplicate.
    /// </summary>
    public string GateName { get; }

    /// <summary>
    /// Optional pre-filter — a provider that runs a long command for
    /// every work item would starve the rest of the registry. Returning
    /// <see langword="false"/> here short-circuits the evaluation and
    /// the host treats the gate as <see cref="GateVerdict.Pending"/> for
    /// the work item (no record written, no event stamped).
    /// </summary>
    /// <param name="context">The per-call evaluation context.</param>
    public bool AppliesTo(VerificationContext context);

    /// <summary>
    /// Evaluates the gate for one work item. Implementations MUST be
    /// idempotent — the host MAY re-call a provider for the same
    /// (work item, gate) pair, e.g. on retry after a transient store
    /// failure, and the verdict must converge to the same terminal
    /// value. Implementations MUST NOT throw on a transient upstream
    /// failure — return <see cref="GateVerdict.Pending"/> with an
    /// empty evidence list and log a warning.
    /// </summary>
    /// <param name="context">The per-call evaluation context.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task<GateVerdictResult> EvaluateAsync(
        VerificationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Producer hook for gates that need to schedule an external
    /// artefact before <see cref="EvaluateAsync"/> can stamp a verdict
    /// (add-orchestra §3 — Coda, task 3.2 "first registered provider is
    /// the existing Verify module"). The default implementation is a
    /// no-op — providers that have nothing to schedule let the base
    /// method run and the engine treats them as a pure read-side gate.
    /// The platform-shipped first provider
    /// (<c>GenericCommandGateProvider</c>) overrides this hook to insert
    /// a <c>GenericCommandRun</c> for the work item on the first
    /// evaluation pass; subsequent passes are idempotent on the
    /// underlying partial index.
    /// </summary>
    /// <param name="context">The per-call evaluation context — same shape <see cref="EvaluateAsync"/> sees.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public Task EnsureGateRunAsync(
        VerificationContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
