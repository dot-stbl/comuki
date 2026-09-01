namespace Comuki.Shared.Contracts.Memory;

/// <summary>
/// Port to the one shared digest service (variant Z): both brain callers —
/// the chat graph and orchestration auto-replan — assemble the brain context
/// through this port so what was fed is journaled identically. Implemented by
/// the memory store slice; a fallback empty digest covers hosts without it.
/// </summary>
public interface IMemoryDigest
{
    /// <summary>
    /// Builds the digest text to inject into the brain context: top-K facts
    /// by cosine + freshest for the scope, rendered for a prompt.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public Task<string> BuildDigestAsync(MemoryDigestRequest request, CancellationToken cancellationToken = default);
}
