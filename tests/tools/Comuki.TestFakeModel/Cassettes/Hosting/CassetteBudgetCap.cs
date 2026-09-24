namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>
/// Resolved per-run USD cap — local, host-free, stand-alone — parallels
/// the runner-side <c>Comuki.AgentTest.Runner.Execution.Budget.BudgetCap</c>
/// with the same semantics so the recording path can reject requests in
/// lock-step with the runner's post-run assertion. Independent of any
/// IConfiguration / DI wiring.
/// </summary>
public readonly record struct BudgetCap
{
    private readonly long? usdMicros;

    /// <summary>
    /// Public factory for the bounded form. The <c>null</c> overload
    /// (<see cref="Unlimited"/>) is the other valid shape — use
    /// that for "no cap in force" so the tracker short-circuits
    /// <see cref="IsOverBudget"/> to false.
    /// </summary>
    /// <param name="usdMicros">A finite micro-USD cap; null = unlimited (use the static field).</param>
    public BudgetCap(long? usdMicros)
    {
        this.usdMicros = usdMicros;
    }

    /// <summary>The effective cap in micro-USD; null means "no cap in force".</summary>
    public long? UsdMicros => usdMicros;

    /// <summary>The "no cap is in force" sentinel.</summary>
    public static BudgetCap Unlimited { get; } = new(usdMicros: null);

    /// <summary>True iff the cap is set and <paramref name="observedUsdMicros"/> strictly exceeds it.</summary>
    public bool IsOverBudget(long observedUsdMicros)
    {
        return usdMicros is { } capMicros && observedUsdMicros > capMicros;
    }

    /// <summary>True iff a finite micro-USD bound is in force.</summary>
    public bool IsBounded => usdMicros is not null;
}
