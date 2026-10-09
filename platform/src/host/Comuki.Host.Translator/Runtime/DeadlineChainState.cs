namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Process-level state for the <c>DeadlinePolicy</c>'s
/// turn-budget chain (harden-worker-runtime Phase 1, design D2).
/// The pump disposes the <c>DeadlinePolicy</c> every cycle, but
/// the chain is "inside the same worker process" — the counter
/// must survive policy recreations, so it lives on a
/// <see cref="DeadlineChainState"/> instance owned by the
/// long-lived <c>TranslatorLoop</c>, not on the per-cycle
/// <c>WorkerRun</c> or the per-cycle policy instance.
/// <para>
/// Lifecycle:
/// <list type="bullet">
///   <item>Created once at <c>TranslatorLoop</c> construction (or
///   the equivalent long-lived process object).</item>
///   <item>Incremented by the <c>DeadlinePolicy</c> on every tick
///   that observes <c>turn_elapsed &gt;= TurnBudget</c>; the
///   policy reads <see cref="ConsecutiveTurnBreachesBeforeFail"/>
///   to decide when to fail the item.</item>
///   <item>Reset to zero on a successful cycle completion by the
///   loop (the same place that calls <c>DeadlinePolicy.ResetBreachCounter</c>).
///   The pump's catch-OCE branch (cancellation) does not reset it
///   — cancellation is the watchdog / orchestrator's signal, not
///   a clean turn.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="consecutiveTurnBreachesBeforeFail">The threshold the policy reads to decide when to fail the item. Bound from <c>TranslatorOptions.ConsecutiveTurnBreachesBeforeFail</c>.</param>
public sealed class DeadlineChainState(int consecutiveTurnBreachesBeforeFail)
{
    /// <summary>Number of consecutive turn-budget breaches observed by the current process's <c>DeadlinePolicy</c> instances.</summary>
    public int ConsecutiveBreaches { get; set; }

    /// <summary>The threshold the policy reads to decide when to fail the item.</summary>
    public int ConsecutiveTurnBreachesBeforeFail { get; } = consecutiveTurnBreachesBeforeFail;
}
