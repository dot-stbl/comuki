namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// One secret backend — env vars, files, or a future remote KV. Each
/// implementation owns a single <see cref="Scheme"/>; the
/// <see cref="CompositeSecretResolver"/> dispatches the reference to the
/// matching provider by prefix. Async because Vault / Consul / file
/// watchers are all I/O — sync wrappers would deadlock on the first
/// remote call (issue #52).
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Stable prefix that identifies this provider (e.g. <c>env</c>,
    /// <c>file</c>, <c>vault</c>). Routes the parsed <see cref="SecretRef"/>
    /// to the right provider. Must be lowercase ASCII.
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Resolves a parsed reference. Returns <c>null</c> when the source
    /// carries no value (env unset, file missing, KV key absent); the
    /// resolver turns the null into a typed <see cref="SecretRefUnsetException"/>
    /// when the call site needs a hard error.
    /// </summary>
    /// <param name="reference">Parsed reference; <see cref="SecretRef.Scheme"/> always equals <see cref="Scheme"/>.</param>
    /// <param name="cancellationToken"></param>
    public Task<string?> ResolveAsync(SecretRef reference, CancellationToken cancellationToken = default);
}
