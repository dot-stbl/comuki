using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// A Project's view of one Repository attached through a role + access
/// pair — the per-Project edge that the cross-repo DAG and the worker
/// workspace resolution consume. The same Repository may be attached to
/// many Projects; each attachment carries its own role, declared access
/// and optional credential override independently (the spec's
/// "attachments are independent per Project" contract).
/// <para>
/// Mutation is PATCH-only: a null <c>role</c> / <c>access</c> /
/// <see cref="CredentialOverrideRef"/> argument in <see cref="Update"/>
/// leaves the stored value untouched, mirroring
/// <c>DomainTypeAdmission.Update</c> and <c>Project.Update</c>.
/// </para>
/// </summary>
public sealed class ProjectRepositoryAttachment
{
    internal ProjectRepositoryAttachment()
    {
    }

    /// <summary>Strong-typed attachment id (UUIDv7).</summary>
    public ProjectRepositoryAttachmentId Id { get; private set; }

    /// <summary>Project the attachment belongs to.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Opaque id of the attached Repository — same <see cref="Guid"/> bytes the Repositories module uses for its own id; the modules never reference each other.</summary>
    public RepositoryId RepositoryId { get; private set; }

    /// <summary>Normalized role (trim + lower-case, invariant culture); open-set value type.</summary>
    public AttachmentRole Role { get; private set; }

    /// <summary>Declared access level; effective access is resolved by the Repositories module from this and the credential's default.</summary>
    public AttachmentAccess Access { get; private set; }

    /// <summary>Optional per-attachment credential override ref (opaque integration key).</summary>
    public string? CredentialOverrideRef { get; private set; }

    /// <summary>When the attachment was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Upper bound of the credential override ref column.</summary>
    public const int CredentialOverrideRefMaxLength = 256;

    /// <summary>
    /// Creates a new attachment. The role is normalized through
    /// <see cref="AttachmentRole.FromWire"/>; an access of
    /// <see cref="AttachmentAccess.Unspecified"/> is rejected loudly
    /// (<see cref="AttachmentDomainException"/>, code
    /// <see cref="AttachmentDomainException.AccessInvalid"/>) — a default
    /// struct must never silently become a working attachment.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="repositoryId"></param>
    /// <param name="role"></param>
    /// <param name="access"></param>
    /// <param name="credentialOverrideRef"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static ProjectRepositoryAttachment Create(
        ProjectId projectId,
        RepositoryId repositoryId,
        string role,
        AttachmentAccess access,
        string? credentialOverrideRef,
        DateTimeOffset now)
    {
        return access == AttachmentAccess.Unspecified
            ? throw new AttachmentDomainException(
                AttachmentDomainException.AccessInvalid,
                "attachment access must not be Unspecified.")
            : new ProjectRepositoryAttachment
            {
                Id = ProjectRepositoryAttachmentId.New(),
                ProjectId = projectId,
                RepositoryId = repositoryId,
                Role = AttachmentRole.FromWire(role),
                Access = access,
                CredentialOverrideRef = credentialOverrideRef,
                CreatedAt = now,
                UpdatedAt = now,
            };
    }

    /// <summary>
    /// PATCH-style update: a null argument leaves the stored value
    /// untouched, mirroring <c>DomainTypeAdmission.Update</c>. A non-null
    /// <paramref name="access"/> equal to <see cref="AttachmentAccess.Unspecified"/>
    /// is rejected with <see cref="AttachmentDomainException"/>.
    /// </summary>
    /// <param name="role"></param>
    /// <param name="access"></param>
    /// <param name="credentialOverrideRef"></param>
    /// <param name="now"></param>
    public void Update(
        string? role,
        AttachmentAccess? access,
        string? credentialOverrideRef,
        DateTimeOffset now)
    {
        if (role is { } nextRole)
        {
            Role = AttachmentRole.FromWire(nextRole);
        }

        if (access is { } nextAccess)
        {
            Access = nextAccess == AttachmentAccess.Unspecified
                ? throw new AttachmentDomainException(
                    AttachmentDomainException.AccessInvalid,
                    "attachment access must not be Unspecified.")
                : nextAccess;
        }

        if (credentialOverrideRef is not null)
        {
            CredentialOverrideRef = credentialOverrideRef;
        }

        UpdatedAt = now;
    }
}
