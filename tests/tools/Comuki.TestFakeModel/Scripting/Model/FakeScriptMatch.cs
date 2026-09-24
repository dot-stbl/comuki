namespace Comuki.TestFakeModel.Scripting.Model;

/// <summary>
/// An optional content predicate an entry can declare — validated against
/// the request that would consume it. A mismatch fails loudly (a
/// <c>FakeScriptException</c>, turned into a 500 Anthropic-shaped error
/// body) rather than silently serving the wrong turn — the same
/// fail-closed philosophy design.md gives replay mode, applied to fake
/// mode's own script authoring mistakes.
/// </summary>
public sealed record FakeScriptMatch(string? LastUserMessageContains, bool? HasToolResult)
{
    /// <summary>True when <paramref name="observed"/> satisfies every predicate this match declares.</summary>
    public bool Matches(ObservedRequest observed)
    {
        return (LastUserMessageContains is null
                || (observed.LastUserMessageText is not null
                    && observed.LastUserMessageText.Contains(LastUserMessageContains, StringComparison.Ordinal)))
            && (HasToolResult is null || HasToolResult == observed.HasToolResult);
    }
}
