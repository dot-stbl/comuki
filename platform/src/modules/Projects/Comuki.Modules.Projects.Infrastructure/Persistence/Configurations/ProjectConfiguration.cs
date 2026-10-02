using Comuki.Modules.Projects.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Configurations;

/// <summary>Projects table mapping: uuid id, unique slug, nullable git fields, soft-archive columns.</summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable(ProjectsDatabase.Projects, ProjectsDatabase.Schema);
        builder.HasKey(static project => project.Id);

        builder.Property(static project => project.Id)
            .HasColumnName("id")
            .HasConversion(ProjectsIdConverters.ProjectIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static project => project.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static project => project.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static project => project.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        builder.Property(static project => project.ProfilesGitUrl)
            .HasColumnName("profiles_git_url")
            .HasMaxLength(2048);

        builder.Property(static project => project.ProfilesGitRef)
            .HasColumnName("profiles_git_ref")
            .HasMaxLength(256);

        builder.Property(static project => project.SourceGitUrl)
            .HasColumnName("source_git_url")
            .HasMaxLength(2048);

        builder.Property(static project => project.SourceGitRef)
            .HasColumnName("source_git_ref")
            .HasMaxLength(256);

        // Identity columns (design D1): icon and colour bounds (200 / fixed 7)
        // are schema-level — varchar(n) mirrors the validation limits; tags
        // is a native Postgres text[] mirroring the admission arrays, with
        // the database default letting the column land on existing rows as
        // an empty list.
        builder.Property(static project => project.Icon)
            .HasColumnName("icon")
            .HasMaxLength(200);

        builder.Property(static project => project.Color)
            .HasColumnName("color")
            .HasMaxLength(7);

        builder.Property(static project => project.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'")
            .IsRequired();

        // Environment-class id (add-worker-environments task 2.2): the
        // scalar stand-in for the project's source repository catalog
        // binding. Nullable (empty binding → implement items stay
        // unclaimable); varchar(128) mirrors Project.MaxEnvClassLength.
        // Move-onto-Repository hand-off is tracked in the add-multi-repo-projects
        // change — this column becomes a temporary stand-in.
        builder.Property(static project => project.EnvClass)
            .HasColumnName("env_class")
            .HasMaxLength(Project.MaxEnvClassLength);

        builder.Property(static project => project.Archived)
            .HasColumnName("archived");

        builder.Property(static project => project.ArchivedAt)
            .HasColumnName("archived_at");

        builder.Property(static project => project.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static project => project.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasIndex(static project => project.Slug)
            .IsUnique()
            .HasDatabaseName("ix_projects_slug");
    }
}
