namespace Comuki.TestFakeModel.Determinism;

/// <summary>
/// Fallback token-usage numbers a scripted response reports when its entry
/// doesn't specify <c>usage</c> — fixed constants, not derived from
/// request/response content, so they stay deterministic across runs and
/// double as an easy-to-recognize "unscripted usage" signal in assertions.
/// Cost-ceiling assertions that care about exact numbers should script
/// <c>usage</c> explicitly (design.md's "Determinism knobs": usage comes
/// from the fake script).
/// </summary>
public static class UsageDefaults
{
    /// <summary>Default <c>usage.input_tokens</c> when a scripted entry omits it.</summary>
    public const int DefaultInputTokens = 50;

    /// <summary>Default <c>usage.output_tokens</c> when a scripted entry omits it.</summary>
    public const int DefaultOutputTokens = 20;
}
