using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Truth table of <see cref="ProjectRepositoryAttachment"/> — the per-Project
/// view of one Repository attached through a role + access pair. The same
/// Repository may be attached to many Projects; each attachment is
/// independent (spec scenario 1 — A's <c>primary</c>/Write and B's
/// <c>library</c>/Read stay independent). <see cref="AttachmentAccess.Unspecified"/>
/// is a rejected default on Create and on PATCH (a default struct must never
/// silently become a working attachment).
/// </summary>
public sealed class ProjectRepositoryAttachmentShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a fresh attachment, when Create runs, then role is normalized, access is preserved, ids are fresh and timestamps match")]
    public void CreateNormalizesRoleAndPreservesAccess()
    {
        var projectId = ProjectId.New();
        var repositoryId = RepositoryId.New();

        var attachment = ProjectRepositoryAttachment.Create(
            projectId,
            repositoryId,
            "  Primary ",
            AttachmentAccess.Write,
            "integration-a",
            now);

        attachment.Id.Value.ShouldNotBe(Guid.Empty);
        attachment.ProjectId.ShouldBe(projectId);
        attachment.RepositoryId.ShouldBe(repositoryId);
        attachment.Role.ShouldBe(AttachmentRole.Primary);
        attachment.Access.ShouldBe(AttachmentAccess.Write);
        attachment.CredentialOverrideRef.ShouldBe("integration-a");
        attachment.CreatedAt.ShouldBe(now);
        attachment.UpdatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given Unspecified access, when Create runs, then AttachmentDomainException with code attachment_access_invalid is thrown")]
    public void CreateRefusesUnspecifiedAccess()
    {
        var exception = Should.Throw<AttachmentDomainException>(static () =>
            ProjectRepositoryAttachment.Create(
                ProjectId.New(),
                RepositoryId.New(),
                "primary",
                AttachmentAccess.Unspecified,
                null,
                now));

        exception.Code.ShouldBe(AttachmentDomainException.AccessInvalid);
    }

    [Fact(DisplayName = "Given a stored attachment, when Update passes nulls, then the stored fields survive and only the stamp moves")]
    public void UpdateLeavesNullFieldsUntouched()
    {
        var attachment = ProjectRepositoryAttachment.Create(
            ProjectId.New(),
            RepositoryId.New(),
            "primary",
            AttachmentAccess.Write,
            "integration-a",
            now);
        var later = now.AddHours(3);

        attachment.Update(null, null, null, later);

        attachment.Role.ShouldBe(AttachmentRole.Primary);
        attachment.Access.ShouldBe(AttachmentAccess.Write);
        attachment.CredentialOverrideRef.ShouldBe("integration-a");
        attachment.UpdatedAt.ShouldBe(later);
        attachment.CreatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a stored attachment, when Update patches every field, then all three are applied and the stamp moves")]
    public void UpdatePatchesAllFields()
    {
        var attachment = ProjectRepositoryAttachment.Create(
            ProjectId.New(),
            RepositoryId.New(),
            "primary",
            AttachmentAccess.Write,
            "integration-a",
            now);
        var later = now.AddMinutes(5);

        attachment.Update("library", AttachmentAccess.Read, "integration-b", later);

        attachment.Role.ShouldBe(AttachmentRole.Library);
        attachment.Access.ShouldBe(AttachmentAccess.Read);
        attachment.CredentialOverrideRef.ShouldBe("integration-b");
        attachment.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given an Unspecified access on Update, when the patch is applied, then AttachmentDomainException with code attachment_access_invalid is thrown")]
    public void UpdateRefusesUnspecifiedAccess()
    {
        var attachment = ProjectRepositoryAttachment.Create(
            ProjectId.New(),
            RepositoryId.New(),
            "primary",
            AttachmentAccess.Write,
            null,
            now);

        var exception = Should.Throw<AttachmentDomainException>(
            () => attachment.Update(null, AttachmentAccess.Unspecified, null, now.AddMinutes(1)));

        exception.Code.ShouldBe(AttachmentDomainException.AccessInvalid);
        attachment.Access.ShouldBe(AttachmentAccess.Write);
    }

    [Fact(DisplayName = "Given two attachments to the same Repository from different Projects (spec scenario 1), when one is mutated, the other keeps its Role, Access and CredentialOverrideRef")]
    public void TwoAttachmentsToSameRepositoryAreIndependent()
    {
        var sharedRepository = RepositoryId.New();
        var projectA = ProjectId.New();
        var projectB = ProjectId.New();

        var attachmentA = ProjectRepositoryAttachment.Create(
            projectA,
            sharedRepository,
            "primary",
            AttachmentAccess.Write,
            null,
            now);
        var attachmentB = ProjectRepositoryAttachment.Create(
            projectB,
            sharedRepository,
            "library",
            AttachmentAccess.Read,
            "integration-b",
            now);

        attachmentA.Id.ShouldNotBe(attachmentB.Id);
        attachmentA.ProjectId.ShouldNotBe(attachmentB.ProjectId);

        attachmentA.Update("service", AttachmentAccess.External, "integration-a", now.AddMinutes(2));

        // attachmentA mutated; attachmentB's fields are untouched.
        attachmentA.Role.ShouldBe(AttachmentRole.Service);
        attachmentA.Access.ShouldBe(AttachmentAccess.External);
        attachmentA.CredentialOverrideRef.ShouldBe("integration-a");

        attachmentB.Role.ShouldBe(AttachmentRole.Library);
        attachmentB.Access.ShouldBe(AttachmentAccess.Read);
        attachmentB.CredentialOverrideRef.ShouldBe("integration-b");
    }
}
