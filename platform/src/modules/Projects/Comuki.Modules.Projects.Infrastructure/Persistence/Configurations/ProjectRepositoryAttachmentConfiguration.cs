using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Modules.Projects.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>project_repository_attachments</c> mapping: uuid surrogate id
/// (UUIDv7), uuid project FK (cascade with the project), uuid
/// <c>repository_id</c> with NO foreign key — the Repository row lives in
/// the sibling Repositories module's <c>repositories</c> schema and the
/// modular-monolith law 3 keeps the two modules strictly independent;
/// the column is a value reference, both types wrap the same
/// <see cref="Guid"/> bytes and the host composition root is the only
/// place that translates between them. Role + access are stored as
/// varchar through the smart-type's wire-form converters; the unique
/// index <c>ux_project_repository_attachments_project_repository</c>
/// enforces "one attachment per (project, repository) pair".
/// </summary>
public sealed class ProjectRepositoryAttachmentConfiguration : IEntityTypeConfiguration<ProjectRepositoryAttachment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProjectRepositoryAttachment> builder)
    {
        builder.ToTable(ProjectsDatabase.ProjectRepositoryAttachments, ProjectsDatabase.Schema);
        builder.HasKey(static attachment => attachment.Id);

        builder.Property(static attachment => attachment.Id)
            .HasColumnName("id")
            .HasConversion(ProjectsIdConverters.ProjectRepositoryAttachmentIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static attachment => attachment.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(ProjectsIdConverters.ProjectIdToUuid);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(static attachment => attachment.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cross-module value reference: same Guid bytes the Repositories
        // module stores on its own rows, but no FK (sibling-isolation
        // law) and no cross-schema reference.
        builder.Property(static attachment => attachment.RepositoryId)
            .HasColumnName("repository_id")
            .HasConversion(ProjectsIdConverters.RepositoryIdToUuid);

        builder.Property(static attachment => attachment.Role)
            .HasColumnName("role")
            .HasMaxLength(AttachmentRole.MaxLength)
            .HasConversion(
                static role => role.Value,
                static wire => AttachmentRole.FromWire(wire))
            .IsRequired();

        builder.Property(static attachment => attachment.Access)
            .HasColumnName("access")
            .HasMaxLength(32)
            .HasConversion(
                static access => access.Value,
                static wire => AttachmentAccess.FromWire(wire))
            .IsRequired();

        builder.Property(static attachment => attachment.CredentialOverrideRef)
            .HasColumnName("credential_override_ref")
            .HasMaxLength(ProjectRepositoryAttachment.CredentialOverrideRefMaxLength);

        builder.Property(static attachment => attachment.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static attachment => attachment.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static attachment => new { attachment.ProjectId, attachment.RepositoryId })
            .IsUnique()
            .HasDatabaseName("ux_project_repository_attachments_project_repository");
    }
}
