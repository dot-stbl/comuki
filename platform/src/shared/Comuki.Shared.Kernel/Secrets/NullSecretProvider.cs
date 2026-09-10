namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Always returns <c>null</c>. Reserved for tests (replace the real env
/// / file provider with this and every <c>ResolveAsync</c> short-circuits)
/// and as a default DI fallback when no provider is configured for a
/// scheme — a production deployment without Vault still receives a
/// <see cref="SecretRefUnsetException"/> on a <c>vault:</c> reference,
/// never a missing-service crash. Singleton.
/// </summary>
public sealed class NullSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Scheme => "null";

    /// <inheritdoc />
    public Task<string?> ResolveAsync(SecretRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
