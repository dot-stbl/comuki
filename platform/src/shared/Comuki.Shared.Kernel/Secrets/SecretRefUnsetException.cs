namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Typed exception thrown when a secret reference resolves to <c>null</c>
/// — the env var is unset, the file is missing, or the KV key is absent.
/// Renamed from <c>SecretEnvRefUnsetException</c> in issue #52 because
/// "env" no longer covers the surface: file / vault / consul refs hit
/// the same path. The composition-root problem-handler registry answers
/// it with a 400 (<c>intake.secret_env_ref_unset</c>) — a misconfigured
/// reference is the operator's mistake, not a server fault.
/// </summary>
/// <param name="reference">The original operator reference (e.g. <c>env:GH_TOKEN</c>).</param>
public sealed class SecretRefUnsetException(string reference)
    : Exception($"secret reference '{reference}' is unset on the host");
