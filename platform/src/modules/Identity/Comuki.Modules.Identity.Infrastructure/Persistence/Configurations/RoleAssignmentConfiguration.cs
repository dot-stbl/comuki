using Comuki.Modules.Identity.Domain.Assignments;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Role assignments table mapping. The "one active assignment per
/// subject+role+scope" invariant is two partial unique indexes: platform
/// rows (scope_project_id IS NULL) need their own index because Postgres
/// treats NULLs as distinct inside a single unique index.
/// </summary>
public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    // ValueConverter lambdas are expression trees — no throw/switch nodes.
    // The parse-or-throw bodies live in the file-static helper behind them.
    private static readonly ValueConverter<Role, string> roleToString = new(
        static role => RoleKeys.Key(role),
        static key => RoleAssignmentKeyParsers.ParseRole(key));

    private static readonly ValueConverter<ScopeLevel, string> scopeLevelToString = new(
        static level => ScopeLevelKeys.Key(level),
        static key => RoleAssignmentKeyParsers.ParseScopeLevel(key));

    private static readonly ValueConverter<SubjectType, string> subjectTypeToString = new(
        static type => SubjectTypeKeys.Key(type),
        static key => RoleAssignmentKeyParsers.ParseSubjectType(key));

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable(IdentityDatabase.RoleAssignments, IdentityDatabase.Schema);
        builder.HasKey(static assignment => assignment.Id);

        builder.Property(static assignment => assignment.Id)
            .HasColumnName("id")
            .HasConversion(IdentityIdConverters.RoleAssignmentIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static assignment => assignment.SubjectType)
            .HasColumnName("subject_type")
            .HasConversion(subjectTypeToString)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(static assignment => assignment.SubjectId)
            .HasColumnName("subject_id");

        builder.Property(static assignment => assignment.Role)
            .HasColumnName("role")
            .HasConversion(roleToString)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static assignment => assignment.ScopeLevel)
            .HasColumnName("scope_level")
            .HasConversion(scopeLevelToString)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static assignment => assignment.ScopeProjectId)
            .HasColumnName("scope_project_id")
            .HasConversion(IdentityIdConverters.ProjectIdToUuid);

        builder.Property(static assignment => assignment.GrantedByType)
            .HasColumnName("granted_by_type")
            .HasConversion(subjectTypeToString)
            .HasMaxLength(8);

        builder.Property(static assignment => assignment.GrantedById)
            .HasColumnName("granted_by_id");

        builder.Property(static assignment => assignment.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static assignment => assignment.RevokedAt)
            .HasColumnName("revoked_at");

        builder.HasIndex(static assignment => new { assignment.SubjectType, assignment.SubjectId })
            .HasDatabaseName("ix_role_assignments_subject")
            .HasFilter("revoked_at IS NULL");

        builder.HasIndex(static assignment => new { assignment.SubjectType, assignment.SubjectId, assignment.Role })
            .IsUnique()
            .HasDatabaseName("ix_role_assignments_active_platform")
            .HasFilter("revoked_at IS NULL AND scope_project_id IS NULL");

        builder.HasIndex(
                static assignment => new { assignment.SubjectType, assignment.SubjectId, assignment.Role, assignment.ScopeProjectId })
            .IsUnique()
            .HasDatabaseName("ix_role_assignments_active_project")
            .HasFilter("revoked_at IS NULL AND scope_project_id IS NOT NULL");
    }
}

/// <summary>
/// Parse-or-throw bodies for the assignment key converters — plain
/// methods, because the converter lambdas themselves are expression
/// trees that cannot contain throw or switch nodes.
/// </summary>
file static class RoleAssignmentKeyParsers
{
    public static Role ParseRole(string key)
    {
        return RoleKeys.Parse(key)
            ?? throw new InvalidOperationException($"unknown role key '{key}' in role_assignments");
    }

    public static ScopeLevel ParseScopeLevel(string key)
    {
        return ScopeLevelKeys.Parse(key)
            ?? throw new InvalidOperationException($"unknown scope level '{key}' in role_assignments");
    }

    public static SubjectType ParseSubjectType(string key)
    {
        return key switch
        {
            SubjectTypeKeys.User => SubjectType.User,
            SubjectTypeKeys.ApiKey => SubjectType.ApiKey,
            _ => throw new InvalidOperationException($"unknown subject type '{key}' in role_assignments"),
        };
    }
}
