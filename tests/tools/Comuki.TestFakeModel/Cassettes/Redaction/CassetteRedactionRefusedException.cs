namespace Comuki.TestFakeModel.Cassettes.Redaction;

/// <summary>
/// Thrown when <see cref="CassetteRedactor"/> can't fully classify a
/// captured response — an object key outside its known-field allowlist.
/// Fail-closed (design.md): the recorder refuses to write the cassette
/// entry rather than guess it's harmless, so <see cref="Recording.CassetteRecordingState"/>
/// propagates this rather than catching and writing anyway.
/// </summary>
public sealed class CassetteRedactionRefusedException(string message) : Exception(message);
