using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Domain.Users;

/// <summary>
/// Links an external OIDC identity (provider + subject claim) to a local
/// user. OIDC answers "who are you"; permissions stay in Comuki
/// assignments (scope-draft §10) — the IdP is not the RBAC source.
/// </summary>
public sealed class OidcLink
{
    internal OidcLink()
    {
    }

    /// <summary>Strong-typed link id (UUIDv7).</summary>
    public OidcLinkId Id { get; private set; }

    /// <summary>The local user the external identity maps to.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Provider name as configured in <c>auth:oidc:providers</c>.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>The <c>sub</c> claim — stable per provider.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>When the link was first made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a link row.</summary>
    /// <param name="userId"></param>
    /// <param name="provider"></param>
    /// <param name="subject"></param>
    /// <param name="now"></param>
    public static OidcLink Create(UserId userId, string provider, string subject, DateTimeOffset now)
    {
        return new OidcLink
        {
            Id = OidcLinkId.New(),
            UserId = userId,
            Provider = provider.Trim().ToLowerInvariant(),
            Subject = subject.Trim(),
            CreatedAt = now,
        };
    }
}
