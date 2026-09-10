namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Resolves an operator-facing reference string by routing the parsed
/// <see cref="SecretRef"/> to the matching <see cref="ISecretProvider"/>.
/// A null / whitespace reference short-circuits to <c>null</c> (the
/// rotation path uses this to skip the resolver). A non-empty reference
/// that the chosen provider returns <c>null</c> for surfaces as
/// <see cref="SecretRefUnsetException"/>; an unknown scheme as
/// <see cref="SecretRefFormatException"/>; a parsed scheme with no
/// registered provider as <see cref="SecretRefFormatException"/>
/// (defensive — by write-time validation no such row should exist).
/// Provider lookup happens once at construction time (the DI container
/// resolves <c>IEnumerable&lt;ISecretProvider&gt;</c> at singleton
/// build); the resolver is itself a singleton.
/// </summary>
/// <param name="providers">Every registered provider; indexed by <see cref="ISecretProvider.Scheme"/>.</param>
public sealed class CompositeSecretResolver(IEnumerable<ISecretProvider> providers) : ISecretResolver
{
    private readonly IReadOnlyDictionary<string, ISecretProvider> byScheme = providers
        .ToDictionary(static provider => provider.Scheme, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string? reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var parsed = SecretRefParser.Parse(reference.Trim());
        if (!byScheme.TryGetValue(parsed.Scheme, out var provider))
        {
            throw new SecretRefFormatException(
                $"secret reference scheme '{parsed.Scheme}' has no registered provider; "
                + "check that the matching [Secrets:{provider.Scheme}] section is enabled at startup");
        }

        var value = await provider.ResolveAsync(parsed, cancellationToken);
        return value is { Length: > 0 }
            ? value
            : throw new SecretRefUnsetException(reference);
    }
}
