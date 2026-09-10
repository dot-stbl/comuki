namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Typed exception thrown when a secret reference resolves to <c>null</c>
/// — the env var is unset, the file is missing, or the KV key is absent.
/// Renamed from <c>SecretEnvRefUnsetException</c> in issue #52 because
/// "env" no longer covers the surface: file / vault / consul refs hit
/// the same path. The endpoint surface answers 502 (resolver failure —
/// operator-side misconfiguration) so the dashboard can render a
/// typed <c>secret_ref_unset</c> code pointing at the misconfigured
/// reference rather than a generic 500.
/// </summary>
/// <param name="reference">The original operator reference (e.g. <c>env:GH_TOKEN</c>).</param>
public sealed class SecretRefUnsetException(string reference)
    : Exception($"secret reference '{reference}' is unset on the host");
