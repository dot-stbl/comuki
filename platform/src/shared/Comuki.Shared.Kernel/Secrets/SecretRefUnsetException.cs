namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Typed exception thrown when a secret reference resolves to <c>null</c>
/// — the env var is unset, the file is missing, or the KV key is absent.
/// Renamed from <c>SecretEnvRefUnsetException</c> in issue #52 because
/// "env" no longer covers the surface: file / vault / consul refs hit
/// the same path. The host's <c>ProviderExceptionHandler</c> currently
/// has no dedicated arm for this exception — on the generic HTTP
/// surface it falls through to the catch-all 500 <c>internal.error</c>
/// response (an explicit 502 <c>secret_ref_unset</c> mapping is a known
/// follow-up); the intake endpoint maps it to a 400 with an
/// intake-specific code.
/// </summary>
/// <param name="reference">The original operator reference (e.g. <c>env:GH_TOKEN</c>).</param>
public sealed class SecretRefUnsetException(string reference)
    : Exception($"secret reference '{reference}' is unset on the host");
