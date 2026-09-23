using System.Text.Json;
using Comuki.TestFakeModel.Anthropic;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// An ordered, scripted response sequence a <see cref="FakeModelServer"/>
/// instance serves (design.md's "Fake-model design": <c>fake</c> mode
/// "serves a fakeScript... fully deterministic, zero network"). Two
/// selection modes, chosen once from the entries themselves (see
/// <see cref="FakeModelState"/>): pure sequential — no entry declares
/// <see cref="FakeScriptEntry.RequestIndex"/>, so entries are consumed one
/// per request in list order — or indexed — every entry declares a
/// <see cref="FakeScriptEntry.RequestIndex"/>, and each request is looked
/// up by it. Either mode may additionally pin an entry to a
/// <see cref="FakeScriptEntry.Match"/> content predicate, validated against
/// the request that consumes it.
/// </summary>
public sealed record FakeScript(string ScenarioName, IReadOnlyList<FakeScriptEntry> Entries)
{
    /// <summary>An empty script under <paramref name="scenarioName"/> — the first request fails loudly (script exhausted).</summary>
    public static FakeScript Empty(string scenarioName)
    {
        return new FakeScript(scenarioName, []);
    }
}

/// <summary>
/// One scripted request/response pairing. <see cref="RequestIndex"/> pins
/// this entry to an exact 1-based request number; leave it <c>null</c> for
/// the common sequential case.
/// </summary>
public sealed record FakeScriptEntry(int? RequestIndex, FakeScriptMatch? Match, FakeScriptResponse Response);

/// <summary>
/// An optional content predicate an entry can declare — validated against
/// the request that would consume it. A mismatch fails loudly (a
/// <see cref="FakeScriptException"/>, turned into a 500 Anthropic-shaped
/// error body) rather than silently serving the wrong turn — the same
/// fail-closed philosophy design.md gives replay mode, applied to fake
/// mode's own script authoring mistakes.
/// </summary>
public sealed record FakeScriptMatch(string? LastUserMessageContains, bool? HasToolResult)
{
    /// <summary>True when <paramref name="observed"/> satisfies every predicate this match declares.</summary>
    public bool Matches(ObservedRequest observed)
    {
        var lastUserMessageOk = LastUserMessageContains is null
            || (observed.LastUserMessageText is not null
                && observed.LastUserMessageText.Contains(LastUserMessageContains, StringComparison.Ordinal));

        return lastUserMessageOk && (HasToolResult is null || HasToolResult == observed.HasToolResult);
    }
}

/// <summary>The scripted reply: content blocks plus the stop reason and token usage to report.</summary>
public sealed record FakeScriptResponse(string StopReason, IReadOnlyList<FakeContentBlock> Content, FakeUsage? Usage);

/// <summary>
/// One content block in a scripted response — a closed hierarchy of
/// <see cref="TextBlock"/> and <see cref="ToolUseBlock"/> (the private
/// constructor keeps it closed to this file; consumers pattern-match).
/// </summary>
public abstract record FakeContentBlock
{
    private FakeContentBlock()
    {
    }

    /// <summary>A plain assistant text block.</summary>
    public sealed record TextBlock(string Text) : FakeContentBlock;

    /// <summary>A <c>tool_use</c> block; <paramref name="Input"/> is the tool call's JSON arguments object.</summary>
    public sealed record ToolUseBlock(string Name, JsonElement Input) : FakeContentBlock;
}

/// <summary>
/// Token usage a scripted response reports. Unset fields fall back to
/// <see cref="Determinism.UsageDefaults"/> at response-generation time.
/// </summary>
public sealed record FakeUsage(int? InputTokens = null, int? OutputTokens = null);
