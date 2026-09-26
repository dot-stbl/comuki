using Comuki.Modules.Projects.Domain.Attachments;

namespace Comuki.Modules.Projects.Application.Attachments;

/// <summary>
/// Entity → view mapping, hand-written and pure (no DI, no source generator —
/// the module has no Mapperly toolchain). One home for every projection so
/// handlers stay one-liners.
/// </summary>
public static class ProjectRepositoryAttachmentMapper
{
    /// <summary>Maps an attachment entity to its read model.</summary>
    /// <param name="attachment"></param>
    /// <returns></returns>
    public static ProjectRepositoryAttachmentView ToView(ProjectRepositoryAttachment attachment)
    {
        return new ProjectRepositoryAttachmentView(
            attachment.Id,
            attachment.ProjectId,
            attachment.RepositoryId,
            attachment.Role.Value,
            attachment.Access,
            attachment.CredentialOverrideRef,
            attachment.CreatedAt,
            attachment.UpdatedAt);
    }
}
