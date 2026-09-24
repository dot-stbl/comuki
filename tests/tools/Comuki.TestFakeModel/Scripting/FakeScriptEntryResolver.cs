using Comuki.TestFakeModel.Anthropic;
using Comuki.TestFakeModel.Scripting.Model;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// The script-matching engine (design.md: "matched by call index or by a
/// cheap content predicate"): resolves each request against a
/// <see cref="FakeScript"/> — indexed lookup or sequential consumption,
/// with an optional content-predicate check either way. One instance per
/// <c>FakeModelState</c>, so the sequential cursor is private, per-server
/// state.
/// </summary>
/// <remarks>Creates the resolver for <paramref name="script"/>.</remarks>
internal sealed class FakeScriptEntryResolver(FakeScript script)
{
    private readonly bool indexedMode = script.Entries.Any(static entry => entry.RequestIndex is not null);
    private int cursor;

    /// <summary>
    /// Resolves the script entry for <paramref name="requestIndex"/>
    /// against <paramref name="observed"/>.
    /// </summary>
    /// <exception cref="FakeScriptException">
    /// No entry is left (script exhausted), no entry declares this exact
    /// <paramref name="requestIndex"/> in indexed mode, or the resolved
    /// entry's <see cref="FakeScriptEntry.Match"/> doesn't hold.
    /// </exception>
    public FakeScriptEntry Resolve(int requestIndex, ObservedRequest observed)
    {
        var entry = indexedMode ? ByRequestIndex(requestIndex) : NextInSequence(requestIndex);
        FakeScriptMatchValidator.Validate(entry, observed, requestIndex, script.ScenarioName);
        return entry;
    }

    /// <summary>Resets the sequential cursor — call between tests that reuse one instance.</summary>
    public void Reset()
    {
        cursor = 0;
    }

    internal FakeScriptEntry ByRequestIndex(int requestIndex)
    {
        foreach (var entry in script.Entries)
        {
            if (entry.RequestIndex == requestIndex)
            {
                return entry;
            }
        }

        throw new FakeScriptException(
            $"fakeScript '{script.ScenarioName}' has no entry with requestIndex={requestIndex} (indexed mode: every entry declares requestIndex).");
    }

    internal FakeScriptEntry NextInSequence(int requestIndex)
    {
        return cursor >= script.Entries.Count
            ? throw new FakeScriptException(
                $"fakeScript '{script.ScenarioName}' exhausted: request #{requestIndex} has no scripted entry left "
                + $"({script.Entries.Count} entries scripted).")
            : script.Entries[cursor++];
    }
}

/// <summary>Validates a resolved entry's optional <see cref="FakeScriptMatch"/> against the request that would consume it.</summary>
file static class FakeScriptMatchValidator
{
    public static void Validate(FakeScriptEntry entry, ObservedRequest observed, int requestIndex, string scenarioName)
    {
        if (entry.Match is null || entry.Match.Matches(observed))
        {
            return;
        }

        throw new FakeScriptException(
            $"fakeScript '{scenarioName}': request #{requestIndex} did not satisfy the scripted entry's match "
            + $"(lastUserMessageContains={Describe(entry.Match.LastUserMessageContains)}, hasToolResult={Describe(entry.Match.HasToolResult)}) "
            + $"— observed lastUserMessageText={Describe(observed.LastUserMessageText)}, hasToolResult={observed.HasToolResult}.");
    }

    public static string Describe(object? value)
    {
        return value?.ToString() ?? "<none>";
    }
}
