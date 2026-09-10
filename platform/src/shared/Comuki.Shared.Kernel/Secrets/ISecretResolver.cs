namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Top-level secret-resolution surface. Accepts the operator-facing
/// reference string (<c>GH_TOKEN</c> / <c>env:GH_TOKEN</c> /
/// <c>file:/etc/comuki/gh-token</c> / <c>vault:secret/gh#token</c>),
/// parses it, and dispatches to the matching <see cref="ISecretProvider"/>.
/// Async so the same path serves local env reads (cheap) and remote KV
/// reads (Vault / Consul, future slices) without a sync-over-async
/// deadlock. Bare names default to <c>env</c> so every pre-existing
/// <c>SecretEnvRef</c> row keeps working — issue #52 §Slices 1.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves the reference string to its value. Returns <c>null</c>
    /// for a null/whitespace input (callers can ignore the resolution —
    /// webhook-rotation paths skip the resolver). For a non-empty input
    /// that the env / file / KV cannot satisfy, the implementation
    /// throws <see cref="SecretRefUnsetException"/>; for an unknown
    /// scheme, <see cref="SecretRefFormatException"/>.
    /// </summary>
    /// <param name="reference">
    /// Operator-facing reference. <c>null</c> or whitespace -> <c>null</c>.
    /// <c>GH_TOKEN</c> -> env:GH_TOKEN. <c>env:GH_TOKEN</c> -> env:GH_TOKEN.
    /// <c>file:/path</c> -> file:/path. Unknown scheme -> format exception.
    /// </param>
    /// <param name="cancellationToken"></param>
    public Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default);
}
