using Comuki.Modules.Repositories.Domain.Ids;

namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// Per-Repository credential reference — the integration (git host / tracker
/// connection) the platform uses to authenticate work on the Repository, and
/// the default access level that integration grants. One row per
/// Repository (the PK is the Repository id). The
/// <see cref="EffectiveAccess(RepositoryAccess)"/> method computes the
/// effective access for an attachment as
/// <c>min(attachment.access, DefaultAccess)</c> — the spec's exact contract.
/// </summary>
public sealed class RepositoryCredentialRef
{
    internal RepositoryCredentialRef()
    {
    }

    /// <summary>FK + PK of this credential row; the matching <c>Repository</c> row owns it.</summary>
    public RepositoryId RepositoryId { get; private set; }

    /// <summary>Opaque integration reference (post-#88 — git-host / tracker connection id).</summary>
    public string IntegrationRef { get; private set; } = string.Empty;

    /// <summary>Access level the integration grants by default; only <see cref="RepositoryAccess.Read"/> and <see cref="RepositoryAccess.Write"/> are legal here.</summary>
    public RepositoryAccess DefaultAccess { get; private set; }

    /// <summary>When the credential reference was first recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a credential reference for a Repository. <paramref name="defaultAccess"/>
    /// MUST be <see cref="RepositoryAccess.Read"/> or
    /// <see cref="RepositoryAccess.Write"/>; <see cref="RepositoryAccess.Unspecified"/>
    /// (placeholder default) and <see cref="RepositoryAccess.External"/> (by
    /// definition never resolves a credential) are rejected with
    /// <see cref="RepositoryDomainException"/> carrying the stable code
    /// <see cref="RepositoryDomainException.DefaultAccessInvalid"/>.
    /// </summary>
    /// <param name="repositoryId">Owning Repository id.</param>
    /// <param name="integrationRef">Opaque integration reference (non-empty).</param>
    /// <param name="defaultAccess">Access the integration grants by default.</param>
    /// <param name="now"></param>
    /// <exception cref="RepositoryDomainException"><paramref name="defaultAccess"/> is <see cref="RepositoryAccess.Unspecified"/> or <see cref="RepositoryAccess.External"/>.</exception>
    public static RepositoryCredentialRef Create(
        RepositoryId repositoryId,
        string integrationRef,
        RepositoryAccess defaultAccess,
        DateTimeOffset now)
    {
        CredentialDefaults.EnsureLegalDefault(repositoryId, defaultAccess);

        return new RepositoryCredentialRef
        {
            RepositoryId = repositoryId,
            IntegrationRef = integrationRef,
            DefaultAccess = defaultAccess,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Pure lattice meet: <c>min(attachmentAccess, DefaultAccess)</c>. The
    /// result is the more restrictive of the two — attachments can never
    /// escalate past the credential's default access, and a credential
    /// granting <see cref="RepositoryAccess.Read"/> will hold a
    /// <see cref="RepositoryAccess.Write"/> attachment to <c>Read</c>. When
    /// <paramref name="attachmentAccess"/> is <see cref="RepositoryAccess.External"/>,
    /// the meet is <see cref="RepositoryAccess.External"/> — the spec
    /// scenario "External attachment never resolves a credential" — so no
    /// write or read credential is resolved through this reference.
    /// </summary>
    /// <param name="attachmentAccess">The attachment's declared access.</param>
    /// <returns>The effective access for work initiated through that attachment.</returns>
    public RepositoryAccess EffectiveAccess(RepositoryAccess attachmentAccess)
    {
        return RepositoryAccess.Min(attachmentAccess, DefaultAccess);
    }
}

/// <summary>
/// Predicate helper for <see cref="RepositoryCredentialRef.Create"/>'s
/// default-access gate. File-scoped so the entity keeps a single public
/// surface without a private helper method (<c>code-shape.md</c> §9).
/// </summary>
file static class CredentialDefaults
{
    /// <summary>
    /// Throws <see cref="RepositoryDomainException"/> carrying the stable code
    /// <see cref="RepositoryDomainException.DefaultAccessInvalid"/> when
    /// <paramref name="access"/> is anything other than
    /// <see cref="RepositoryAccess.Read"/> or <see cref="RepositoryAccess.Write"/>.
    /// The placeholder <see cref="RepositoryAccess.Unspecified"/> and the
    /// no-credential <see cref="RepositoryAccess.External"/> are the two
    /// rejects; the spec accepts only <c>Read</c> and <c>Write</c> as a
    /// credential default.
    /// </summary>
    /// <param name="repositoryId"></param>
    /// <param name="access"></param>
    /// <exception cref="RepositoryDomainException"></exception>
    public static void EnsureLegalDefault(RepositoryId repositoryId, RepositoryAccess access)
    {
        if (access == RepositoryAccess.Read || access == RepositoryAccess.Write)
        {
            return;
        }

        throw new RepositoryDomainException(
            RepositoryDomainException.DefaultAccessInvalid,
            $"repository '{repositoryId}' credential default access '{access.Value}' is invalid; expected Read or Write.");
    }
}
