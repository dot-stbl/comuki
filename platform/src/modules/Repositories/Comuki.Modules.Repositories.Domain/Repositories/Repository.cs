using Comuki.Modules.Repositories.Domain.Ids;

namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// A registered git repository (url, host, default branch) — the standalone
/// unit Projects attach to. The (host, url) pair is the dedup key; the unique
/// index on (host, url) is the concurrency arbiter for re-registration —
/// see <c>IRepositoryStore.GetOrRegisterAsync</c> in the Application layer.
/// </summary>
public sealed class Repository
{
    internal Repository()
    {
    }

    /// <summary>Strong-typed id (UUIDv7).</summary>
    public RepositoryId Id { get; private set; }

    /// <summary>Normalized clone url (<c>https://host/owner/repo.git</c>); unique together with <see cref="Host"/>.</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>Normalized host key (<c>github.com</c>, <c>gitlab.com</c>, …).</summary>
    public string Host { get; private set; } = string.Empty;

    /// <summary>Default branch name (e.g. <c>main</c>); trimmed; defaults to <c>main</c> when blank.</summary>
    public string DefaultBranch { get; private set; } = string.Empty;

    /// <summary>When the repository was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Computed (host, url) identity — the dedup key consulted by the store.</summary>
    public RepositoryIdentity Identity => RepositoryIdentity.Of(Host, Url);

    /// <summary>
    /// Normalizes a url or host key the way the stored rows are normalized
    /// (trim + lower-case, invariant culture). Public so callers comparing
    /// identity pairs use the very same key the entity persists.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>Creates a Repository; url/host are normalized here, default branch defaults to <c>main</c>.</summary>
    /// <param name="url">Repository clone url (any case/whitespace).</param>
    /// <param name="host">Host key (<c>github.com</c>, …).</param>
    /// <param name="defaultBranch">Default branch name; blank becomes <c>main</c>.</param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static Repository Create(
        string url,
        string host,
        string defaultBranch,
        DateTimeOffset now)
    {
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim();

        return new Repository
        {
            Id = RepositoryId.New(),
            Url = Normalize(url),
            Host = Normalize(host),
            DefaultBranch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Partial update: a null field leaves the stored value untouched
    /// (PATCH semantics, same as the Projects module's <c>Project.Update</c>).
    /// </summary>
    /// <param name="url">New url (normalized); null keeps the current value.</param>
    /// <param name="host">New host (normalized); null keeps the current value.</param>
    /// <param name="defaultBranch">New default branch (trimmed; blank becomes <c>main</c>); null keeps the current value.</param>
    /// <param name="now"></param>
    public void Update(
        string? url,
        string? host,
        string? defaultBranch,
        DateTimeOffset now)
    {
        if (url is { } nextUrl)
        {
            Url = Normalize(nextUrl);
        }

        if (host is { } nextHost)
        {
            Host = Normalize(nextHost);
        }

        if (defaultBranch is { } nextBranch)
        {
            DefaultBranch = string.IsNullOrWhiteSpace(nextBranch) ? "main" : nextBranch.Trim();
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Pure duplicate-registration guard: true when the (host, url) pair
    /// normalized from the caller's arguments equals the stored
    /// <see cref="Identity"/>. Consulted by the store before a fresh insert
    /// so a re-registration with casing/whitespace variants resolves to the
    /// existing row.
    /// </summary>
    /// <param name="host">Host to compare (any case/whitespace).</param>
    /// <param name="url">Url to compare (any case/whitespace).</param>
    /// <returns>true when normalized identity matches.</returns>
    public bool MatchesIdentity(string host, string url)
    {
        var candidate = RepositoryIdentity.Of(host, url);
        return candidate == Identity;
    }
}
