namespace Comuki.TestFakeModel.Cassettes.Replay;

/// <summary>
/// Per-server-instance replay state: the loaded cassette and a
/// <see cref="CassetteReplayResolver"/> for the matching engine — same
/// shape as <c>Scripting.FakeModelState</c>, registered as a DI singleton
/// scoped to one <c>Hosting.CassetteModelServer</c> instance.
/// </summary>
/// <remarks>Creates the state for <paramref name="cassette"/>.</remarks>
internal sealed class CassettePlaybackState(CassetteFile cassette)
{
    private readonly Lock gate = new();
    private readonly CassetteReplayResolver resolver = new(cassette);
    private int requestCounter;

    /// <summary>The scenario name this cassette was recorded under.</summary>
    public string Scenario { get; } = cassette.Scenario;

    /// <summary>Allocates the next 1-based request index — one call per inbound request.</summary>
    public int NextRequestIndex()
    {
        lock (gate)
        {
            return ++requestCounter;
        }
    }

    /// <summary>Resolves the next exchange for <paramref name="requestIndex"/> — see <see cref="CassetteReplayResolver.Resolve"/>.</summary>
    public CassetteExchange Resolve(int requestIndex, string method, string path, ObservedRequest observed)
    {
        lock (gate)
        {
            return resolver.Resolve(requestIndex, method, path, observed);
        }
    }
}
