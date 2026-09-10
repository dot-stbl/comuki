using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// Resolves the secret reference for a connection write — throws
/// <see cref="SecretRefUnsetException"/> when the host does not have
/// the value set. The check fires at write time because that is when
/// the operator can act on the answer — a saved connection with a
/// missing secret is an unreachable source. Extracted from
/// <see cref="SourceConnectionService"/> so the service stays free of
/// private methods (<c>code-shape.md</c> §1a — only contract overrides
/// may be private).
/// </summary>
/// <param name="resolver">Shared-kernel resolver — routes by scheme to the matching provider.</param>
public sealed class SecretRefResolverGuard(ISecretResolver resolver)
{
    /// <summary>Resolve <paramref name="reference"/>; throw when the host cannot satisfy it.</summary>
    /// <param name="reference">Trimmed operator reference.</param>
    /// <param name="cancellationToken"></param>
    public async Task EnsureResolvableAsync(string reference, CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveAsync(reference, cancellationToken);
        if (string.IsNullOrEmpty(resolved))
        {
            throw new SecretRefUnsetException(reference);
        }
    }
}
