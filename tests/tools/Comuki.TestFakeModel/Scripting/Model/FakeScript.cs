namespace Comuki.TestFakeModel.Scripting.Model;

/// <summary>
/// An ordered, scripted response sequence a <c>FakeModelServer</c>
/// instance serves (design.md's "Fake-model design": <c>fake</c> mode
/// "serves a fakeScript... fully deterministic, zero network"). Two
/// selection modes, chosen once from the entries themselves (see
/// <c>FakeScriptEntryResolver</c>): pure sequential — no entry declares
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
