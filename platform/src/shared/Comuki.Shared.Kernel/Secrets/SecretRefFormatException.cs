namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Typed exception thrown when a secret reference cannot be parsed —
/// the scheme prefix is unknown (<c>foo:BAR</c>) or the reference string
/// is malformed. Surfaced at write-time validation (FluentValidation
/// rules over <c>SecretEnvRef</c>) so a misconfigured row never reaches
/// the request path. The resolver also throws this on a runtime unknown
/// scheme — defensive fallback in case the wire format drifts.
/// </summary>
public sealed class SecretRefFormatException(string message)
    : Exception(message);
