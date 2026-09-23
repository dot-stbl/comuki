using Comuki.TestFakeModel.Cassettes.Matching;

namespace Comuki.TestFakeModel.Cassettes.Replay;

/// <summary>
/// The cassette-matching engine — sequential consumption only (cassettes
/// record real call order; there's no indexed-lookup mode the way
/// <c>Scripting.FakeScriptEntryResolver</c> has for hand-authored
/// fakeScripts), strict on every field: method, path, and the hashed
/// <c>matchOn</c> key must all agree with the next expected exchange.
/// </summary>
/// <remarks>Creates the resolver for <paramref name="cassette"/>.</remarks>
internal sealed class CassetteReplayResolver(CassetteFile cassette)
{
    private int cursor;

    /// <summary>Resolves the next exchange for <paramref name="requestIndex"/>, verifying it against <paramref name="method"/>/<paramref name="path"/>/<paramref name="observed"/>.</summary>
    /// <exception cref="CassetteMismatchException">The cassette is exhausted, or the next expected exchange doesn't match.</exception>
    public CassetteExchange Resolve(int requestIndex, string method, string path, ObservedRequest observed)
    {
        if (cursor >= cassette.Exchanges.Count)
        {
            throw new CassetteMismatchException(
                $"cassette '{cassette.Scenario}' exhausted: request #{requestIndex} has no recorded exchange left ({cassette.Exchanges.Count} exchanges recorded).");
        }

        var expected = cassette.Exchanges[cursor];
        var observedRequest = new CassetteRequest(method, path, CassetteMatchKeyBuilder.Build(observed));
        if (expected.Request != observedRequest)
        {
            throw new CassetteMismatchException(CassetteMismatchDiff.Describe(cassette.Scenario, requestIndex, cursor, expected.Request, observedRequest));
        }

        cursor++;
        return expected;
    }
}

/// <summary>The readable-diff formatting step <see cref="CassetteReplayResolver"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class CassetteMismatchDiff
{
    public static string Describe(string scenario, int requestIndex, int exchangeIndex, CassetteRequest expected, CassetteRequest observed)
    {
        return $"""
            cassette '{scenario}' replay mismatch at exchange #{exchangeIndex} (request #{requestIndex}):
              expected: {Format(expected)}
              observed: {Format(observed)}
            """;
    }

    public static string Format(CassetteRequest request)
    {
        return $"method={request.Method} path={request.Path} lastUserMessageHash={request.MatchOn.LastUserMessageHash ?? "<none>"} toolResultPresent={request.MatchOn.ToolResultPresent}";
    }
}
