using Comuki.Shared.Contracts.Memory;

namespace Comuki.Host.Chat;

/// <summary>
/// Digest fallback for hosts composed without the memory store (sibling
/// slice): an empty digest means "nothing fed", which the turn service
/// treats as "do not journal". Registered with TryAdd so the real digest
/// service wins when present.
/// </summary>
public sealed class EmptyMemoryDigest : IMemoryDigest
{
    /// <inheritdoc />
    public Task<string> BuildDigestAsync(MemoryDigestRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Empty);
    }
}
