namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>
/// Tracker-side bookkeeping for live-mode cost enforcement — local to
/// <c>Comuki.TestFakeModel</c> so the recording server stays
/// dependency-free (does NOT reference
/// <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetTracker</c>).
/// Identical semantics: accumulates spend + token counts and exposes
/// <see cref="IsOverBudget"/>. The runner's own
/// <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetTracker</c> holds the
/// post-run assertion side; this local copy sits in front of the forward
/// path so it can refuse to call the real upstream at all.
/// </summary>
/// <remarks>
/// Why duplicate the type rather than import the runner's: the runner is
/// a downstream tool of the test fixture, not the other way around —
/// adding a ProjectReference from the test's recording server to a test
/// tool's runner would be a layering inversion. The two trackers stay in
/// lock-step via the parent's own <c>BudgetCap.Resolve</c> +
/// <see cref="BudgetTracker"/> outside this project — the WS9 live harness
/// composes both: it owns a runner-side tracker for the post-run check
/// and a local tracker for the pre-forward refusal.
/// </remarks>
/// <remarks>Creates a tracker for <paramref name="cap"/>.</remarks>
/// <param name="cap">The resolved effective cap. Pass <see cref="BudgetCap.Unlimited"/> to disable enforcement.</param>
public sealed class BudgetTracker(BudgetCap cap)
{

    /// <summary>The cap this tracker enforces.</summary>
    public BudgetCap Cap { get; } = cap;

    /// <summary>The accumulated spend as of the last <see cref="Add"/> call (zero until then).</summary>
    public long UsdMicros { get; private set; }

    /// <summary>The accumulated input-token count across <see cref="Add"/> calls (zero until then).</summary>
    public long TokensIn { get; private set; }

    /// <summary>The accumulated output-token count across <see cref="Add"/> calls (zero until then).</summary>
    public long TokensOut { get; private set; }

    /// <summary>True once <see cref="UsdMicros"/> strictly exceeds the cap — never flips when the cap is unlimited.</summary>
    public bool IsOverBudget => Cap.IsOverBudget(UsdMicros);

    /// <summary>Records one exchange's contribution: USD-micro plus input/output token counts; both clamped to non-negative.</summary>
    /// <param name="usdMicrosDelta">The just-computed micro-USD cost of one exchange.</param>
    /// <param name="inputTokensDelta">Input tokens for the same exchange.</param>
    /// <param name="outputTokensDelta">Output tokens for the same exchange.</param>
    public void Add(long usdMicrosDelta, int inputTokensDelta, int outputTokensDelta)
    {
        UsdMicros += usdMicrosDelta < 0 ? 0 : usdMicrosDelta;
        TokensIn += inputTokensDelta < 0 ? 0 : inputTokensDelta;
        TokensOut += outputTokensDelta < 0 ? 0 : outputTokensDelta;
    }
}
