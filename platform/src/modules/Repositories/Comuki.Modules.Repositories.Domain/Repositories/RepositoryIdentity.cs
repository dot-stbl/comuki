namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// The normalized (host, url) pair that uniquely identifies a registered
/// Repository. Host and url are trimmed and lower-cased via
/// <see cref="Of(string, string)"/> so casing/whitespace variants resolve to
/// the same Repository row (the spec's "registering the same (host, url) pair
/// twice SHALL resolve to the existing Repository row").
/// </summary>
/// <param name="Host">Normalized host (trim + lower-invariant).</param>
/// <param name="Url">Normalized url (trim + lower-invariant).</param>
public readonly record struct RepositoryIdentity(string Host, string Url)
{
    /// <summary>Constructs a normalized (host, url) pair from raw user input.</summary>
    /// <param name="host">The git host (<c>github.com</c>, <c>gitlab.com</c>, …).</param>
    /// <param name="url">The repository url.</param>
    /// <returns></returns>
    public static RepositoryIdentity Of(string host, string url)
    {
        return new RepositoryIdentity(
            host.Trim().ToLowerInvariant(),
            url.Trim().ToLowerInvariant());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Host} :: {Url}";
    }
}
