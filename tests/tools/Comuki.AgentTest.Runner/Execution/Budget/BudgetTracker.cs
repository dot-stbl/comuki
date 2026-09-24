using Comuki.AgentTest.Runner.Reporting.Report;

namespace Comuki.AgentTest.Runner.Execution.Budget;

/// <summary>
/// Mutable in-process accumulator of one run's <see cref="RunCost"/>.
/// Single-threaded by contract (the runner calls <see cref="Add"/> from the
/// harness's own forward path, not from parallel reads); no locking
/// overhead. <see cref="IsOverBudget"/> is the hot check the recording
/// path takes before each upstream forward.
/// </summary>
/// <remarks>
/// Why mutable, not pure-functional: the recording server's forward path
/// does this check on every inbound POST, and threading a fresh cost
/// snapshot through the upstack would be a structural change far outside
/// this rule's scope. The harness composes the same instance over both
/// the recording-side pre-forward check and the runner's post-run
/// assertion-side check — both reads, both writers from one direction.
/// </remarks>
/// <remarks>Creates a tracker for <paramref name="cap"/>. Pass <see cref="BudgetCap.Unlimited"/> for no enforcement.</remarks>
/// <param name="cap">The resolved effective cap.</param>
public sealed class BudgetTracker(BudgetCap cap)
{
    private long usdMicros;
    private long tokensIn;
    private long tokensOut;

    /// <summary>The cap this tracker enforces (immutable snapshot).</summary>
    public BudgetCap Cap { get; } = cap;

    /// <summary>The accumulated cost as of the last <see cref="Add"/> call (zero until then).</summary>
    public RunCost Total => new()
    {
        UsdMicros = usdMicros,
        TokensIn = tokensIn,
        TokensOut = tokensOut,
    };

    /// <summary>True once <see cref="Total"/>'s <c>UsdMicros</c> strictly exceeds the cap — <see cref="BudgetCap.IsOverBudget"/>'s definition; never flips when the cap is unlimited.</summary>
    public bool IsOverBudget => Cap.IsOverBudget(usdMicros);

    /// <summary>Records <paramref name="delta"/> against the running total. Negative deltas are clamped to zero — token counts and micro-USD are non-negative cumulatives by contract.</summary>
    /// <param name="delta">Cost contribution of the just-finished exchange.</param>
    public void Add(RunCost delta)
    {
        var extraUsd = delta.UsdMicros;
        if (extraUsd < 0)
        {
            extraUsd = 0;
        }

        var extraIn = delta.TokensIn;
        if (extraIn < 0)
        {
            extraIn = 0;
        }

        var extraOut = delta.TokensOut;
        if (extraOut < 0)
        {
            extraOut = 0;
        }

        usdMicros += extraUsd;
        tokensIn += extraIn;
        tokensOut += extraOut;
    }
}
