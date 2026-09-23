using Comuki.TestFakeModel.Scripting.Model;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Per-server-instance state: the fakeScript being served, the recorded
/// request log, and a <see cref="FakeScriptEntryResolver"/> for the
/// matching engine. One instance per <c>FakeModelServer</c>, registered as
/// a DI singleton scoped to that server's own <c>WebApplication</c> —
/// never a process-wide static, so parallel xUnit suites each get an
/// isolated fake.
/// </summary>
/// <remarks>Creates the state for <paramref name="script"/>, reading timestamps from <paramref name="clock"/>.</remarks>
internal sealed class FakeModelState(FakeScript script, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly List<RecordedRequest> requests = [];
    private readonly FakeScriptEntryResolver resolver = new(script);
    private int requestCounter;

    /// <summary>The fakeScript this instance serves.</summary>
    public FakeScript Script { get; } = script;

    /// <summary>The clock every recorded request's timestamp is read from.</summary>
    public TimeProvider Clock { get; } = clock;

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
            return ++requestCounter;
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

    /// <summary>Resolves the script entry for <paramref name="requestIndex"/> against <paramref name="observed"/> — see <see cref="FakeScriptEntryResolver.Resolve"/>.</summary>
    public FakeScriptEntry Resolve(int requestIndex, ObservedRequest observed)
    {
        lock (gate)
        {
            return resolver.Resolve(requestIndex, observed);
        }
    }

    /// <summary>Clears the recorded request log and rewinds the script cursor — call between tests that reuse one instance.</summary>
    public void Reset()
    {
        lock (gate)
        {
            requests.Clear();
            resolver.Reset();
            requestCounter = 0;
        }
    }
}
