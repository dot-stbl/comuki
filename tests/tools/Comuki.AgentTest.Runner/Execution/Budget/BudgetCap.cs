namespace Comuki.AgentTest.Runner.Execution.Budget;

/// <summary>
/// Resolves the effective per-run USD cap from two optional inputs — the
/// scenario's own <c>budget.maxUsd</c> and a process-wide ceiling set via
/// <c>COMUKI_LIVE_BUDGET_MAX_USD</c>. Stand-alone, host-free pure value
/// type — sits in <c>Comuki.AgentTest.Runner</c> rather than the
/// orchestrator so both the runner and any future offline budget audit can
/// get the same answer from the same inputs (design.md D5: "independent
/// of the target project's own budget gate").
/// </summary>
/// <remarks>
/// <para>Why the SMALLER wins. Two failure modes the rule closes:
/// <list type="bullet">
///   <item>
///     A scenario forgets its own cap, the global ceiling is the only
///     thing standing between a runaway live loop and a paid bill.
///     Smaller wins: the cap that actually fires is whichever one is
///     lower.
///   </item>
///   <item>
///     A scenario's tight cap (say <c>$0.10</c>) somehow becomes looser
///     when a default global ceiling is added. Smaller wins.
///   </item>
/// </list>
/// </para>
/// <para>Resolution order:
/// <list type="number">
///   <item>Both unset — returns <see cref="Unlimited"/>.</item>
///   <item>Only one set — returns that one (parsed).</item>
///   <item>Both set — returns the smaller of the two.</item>
/// </list>
/// </para>
/// <para>
/// A malformed env value is treated as unset (see
/// <see cref="ResolveOptionalUsdMicros"/>), not as a crash. Live runs
/// shouldn't fail because an operator fat-fingered an env value.
/// </para>
/// </remarks>
public readonly record struct BudgetCap
{
    private readonly long? usdMicros;

    private BudgetCap(long? usdMicros)
    {
        this.usdMicros = usdMicros;
    }

    /// <summary>Effective cap in micro-USD. Null means "unlimited" — no cap is in force.</summary>
    public long? UsdMicros => usdMicros;

    /// <summary>The "no cap is in force" sentinel — every <see cref="IsOverBudget"/> caller treats this as false-forever.</summary>
    public static BudgetCap Unlimited { get; } = new(usdMicros: null);

    /// <summary>True iff the cap is set and <paramref name="observedUsdMicros"/> strictly exceeds it.</summary>
    /// <param name="observedUsdMicros">A running cost's micro-USD total.</param>
    public bool IsOverBudget(long observedUsdMicros)
    {
        return usdMicros is { } capMicros && observedUsdMicros > capMicros;
    }

    /// <summary>True iff any cap is set (a finite micro-USD bound is in force).</summary>
    public bool IsBounded => usdMicros is not null;

    /// <summary>
    /// Resolves the effective cap from a scenario's optional per-scenario
    /// cap and a process-wide global ceiling read from an env var name
    /// the caller supplies.
    /// </summary>
    /// <param name="scenarioMaxUsd">Per-scenario <c>budget.maxUsd</c>, or null when unset.</param>
    /// <param name="globalEnvVar">Env-var name to read the process-wide ceiling from. Null/unset/invalid env values are treated as "no global cap".</param>
    public static BudgetCap Resolve(decimal? scenarioMaxUsd, string? globalEnvVar)
    {
        var scenarioMicros = ResolveOptionalUsdMicros(scenarioMaxUsd, nameof(scenarioMaxUsd));
        var globalRaw = string.IsNullOrWhiteSpace(globalEnvVar) ? null : Environment.GetEnvironmentVariable(globalEnvVar);
        var globalMicros = ResolveOptionalUsdMicros(globalRaw, globalEnvVar ?? "globalEnvVar");

        return (scenarioMicros, globalMicros) switch
        {
            (null, null) => Unlimited,
            ({ } s, null) => new BudgetCap(s),
            (null, { } g) => new BudgetCap(g),
            ({ } s, { } g) => new BudgetCap(long.Min(s, g)),
        };
    }

    /// <summary>
    /// Lenient USD-micros resolver for either a <see cref="decimal"/> or a
    /// <see cref="string"/> value. Invalid/negative inputs collapse to
    /// null — the caller treats them as "not set" rather than a hard
    /// crash on a misconfigured run.
    /// </summary>
    /// <param name="value">Either a typed decimal or a string (env-var shape).</param>
    /// <param name="name">Source label used only for the failure message in thrown exceptions when a hard cast fails.</param>
    private static long? ResolveOptionalUsdMicros(object? value, string name)
    {
        return value switch
        {
            null => null,
            decimal d when d < 0m => null,
            decimal d => UsdToMicros(d, name),
            string s when string.IsNullOrWhiteSpace(s) => null,
            string s => ParseUsdMicros(s, name),
            _ => null,
        };
    }

    private static long UsdToMicros(decimal usd, string name)
    {
        var micros = usd * 1_000_000m;
        return (long)decimal.Round(micros, MidpointRounding.AwayFromZero)
            is var rounded and >= 0
                ? rounded
                : throw new InvalidOperationException($"{name}='{usd}' is negative after rounding to micro-USD");
    }

    private static long? ParseUsdMicros(string text, string name)
    {
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        if (parsed < 0m)
        {
            return null;
        }

        try
        {
            return UsdToMicros(parsed, name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
