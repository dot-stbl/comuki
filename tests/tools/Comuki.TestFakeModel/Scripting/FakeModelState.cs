using Comuki.TestFakeModel.Anthropic;
using Comuki.TestFakeModel.Determinism;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Per-server-instance state: the fakeScript being served, the recorded
/// request log, and the script-matching engine (design.md: "matched by
/// call index or by a cheap content predicate"). One instance per
/// <see cref="FakeModelServer"/>, registered as a DI singleton scoped to
/// that server's own <c>WebApplication</c> — never a process-wide static,
/// so parallel xUnit suites each get an isolated fake.
/// </summary>
/// <remarks>Creates the state for <paramref name="script"/>, reading timestamps from <paramref name="clock"/>.</remarks>
internal sealed class FakeModelState(FakeScript script, IClock clock)
{
    private readonly Lock gate = new();
    private readonly List<RecordedRequest> requests = [];
    private readonly bool indexedMode = script.Entries.Any(static entry => entry.RequestIndex is not null);
    private int cursor;
    private int requestCounter;

    /// <summary>The fakeScript this instance serves.</summary>
    public FakeScript Script { get; } = script;

    /// <summary>The clock every recorded request's timestamp is read from.</summary>
    public IClock Clock { get; } = clock;

    /// <summary>The scenario name deterministic ids are derived from.</summary>
    public string ScenarioName => Script.ScenarioName;

    /// <summary>Every request observed so far, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>Allocates the next 1-based request index — one call per inbound <c>POST /v1/messages</c>.</summary>
    public int NextRequestIndex()
    {
        lock (gate)
        {
            requestCounter++;
            return requestCounter;
        }
    }

    /// <summary>Appends <paramref name="request"/> to the recorded log.</summary>
    public void Record(RecordedRequest request)
    {
        lock (gate)
        {
            requests.Add(request);
        }
    }

    /// <summary>
    /// Resolves the script entry for <paramref name="requestIndex"/>
    /// against <paramref name="observed"/>. Indexed mode looks the entry
    /// up by <see cref="FakeScriptEntry.RequestIndex"/>; sequential mode
    /// consumes the next unconsumed entry. Either way, a declared
    /// <see cref="FakeScriptEntry.Match"/> is validated against
    /// <paramref name="observed"/> before the entry is returned.
    /// </summary>
    /// <exception cref="FakeScriptException">
    /// No entry is left (script exhausted), no entry declares this exact
    /// <paramref name="requestIndex"/> in indexed mode, or the resolved
    /// entry's <see cref="FakeScriptEntry.Match"/> doesn't hold.
    /// </exception>
    public FakeScriptEntry Resolve(int requestIndex, ObservedRequest observed)
    {
        lock (gate)
        {
            var entry = indexedMode
                ? ResolveIndexed(requestIndex)
                : ResolveSequential(requestIndex);

            ValidateMatch(entry, observed, requestIndex);
            return entry;
        }
    }

    /// <summary>Clears the recorded request log and rewinds the script cursor — call between tests that reuse one instance.</summary>
    public void Reset()
    {
        lock (gate)
        {
            requests.Clear();
            cursor = 0;
            requestCounter = 0;
        }
    }

    private FakeScriptEntry ResolveIndexed(int requestIndex)
    {
        foreach (var entry in Script.Entries)
        {
            if (entry.RequestIndex == requestIndex)
            {
                return entry;
            }
        }

        throw new FakeScriptException(
            $"fakeScript '{ScenarioName}' has no entry with requestIndex={requestIndex} (indexed mode: every entry declares requestIndex).");
    }

    private FakeScriptEntry ResolveSequential(int requestIndex)
    {
        if (cursor >= Script.Entries.Count)
        {
            throw new FakeScriptException(
                $"fakeScript '{ScenarioName}' exhausted: request #{requestIndex} has no scripted entry left "
                + $"({Script.Entries.Count} entries scripted).");
        }

        var entry = Script.Entries[cursor];
        cursor++;
        return entry;
    }

    private void ValidateMatch(FakeScriptEntry entry, ObservedRequest observed, int requestIndex)
    {
        if (entry.Match is null || entry.Match.Matches(observed))
        {
            return;
        }

        throw new FakeScriptException(
            $"fakeScript '{ScenarioName}': request #{requestIndex} did not satisfy the scripted entry's match "
            + $"(lastUserMessageContains={Describe(entry.Match.LastUserMessageContains)}, hasToolResult={Describe(entry.Match.HasToolResult)}) "
            + $"— observed lastUserMessageText={Describe(observed.LastUserMessageText)}, hasToolResult={observed.HasToolResult}.");
    }

    private static string Describe(object? value)
    {
        return value?.ToString() ?? "<none>";
    }
}
