namespace Comuki.TestFakeModel.Cassettes.Replay;

/// <summary>
/// Thrown when an inbound request can't be resolved against the loaded
/// cassette — no exchange left (cassette exhausted) or the next expected
/// exchange's <c>matchOn</c> doesn't match the observed request. The
/// endpoint turns this into a typed error response (design.md task 5.4:
/// "fails loudly... not a silent passthrough") carrying a readable
/// expected-vs-observed diff, never a silent replay of the wrong turn.
/// </summary>
public sealed class CassetteMismatchException(string message) : Exception(message);
