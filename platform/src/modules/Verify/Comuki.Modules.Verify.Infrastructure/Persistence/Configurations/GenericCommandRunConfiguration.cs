using Comuki.Modules.Verify.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Verify.Infrastructure.Persistence.Configurations;

/// <summary>
/// generic_command_runs mapping: project_id is a nullable uuid (global
/// gate runs leave it null — different context, no FK to the projects
/// schema); executable + output_log are bounded/unbounded text,
/// arguments is a jsonb array (see <see cref="VerifyValueConverters.ArgumentsToJson"/>);
/// status is the smart-type the worker transitions through. The
/// pending-runs query drives the worker poll and is indexed.
/// </summary>
public sealed class GenericCommandRunConfiguration : IEntityTypeConfiguration<GenericCommandRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GenericCommandRun> builder)
    {
        builder.ToTable(VerifyDatabase.GenericCommandRuns, VerifyDatabase.Schema);
        builder.HasKey(static run => run.Id);

        builder.Property(static run => run.Id)
            .HasColumnName("id")
            .HasConversion(VerifyValueConverters.GenericCommandRunIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static run => run.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(VerifyValueConverters.ProjectIdToNullableUuid);

        builder.Property(static run => run.ProfileKey)
            .HasColumnName("profile_key")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(static run => run.Executable)
            .HasColumnName("executable")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static run => run.Arguments)
            .HasColumnName("arguments")
            .HasConversion(VerifyValueConverters.ArgumentsToJson, VerifyValueConverters.ArgumentsComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(static run => run.ExpectedExitCode)
            .HasColumnName("expected_exit_code")
            .IsRequired();

        builder.Property(static run => run.Status)
            .HasColumnName("status")
            .HasConversion(VerifyValueConverters.GenericCommandStatusToString)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static run => run.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(static run => run.StartedAt)
            .HasColumnName("started_at");

        builder.Property(static run => run.FinishedAt)
            .HasColumnName("finished_at");

        builder.Property(static run => run.ActualExitCode)
            .HasColumnName("actual_exit_code");

        builder.Property(static run => run.OutputLog)
            .HasColumnName("output_log")
            .HasColumnType("text")
            .IsRequired();

        // Pending-runs query: WHERE status = 'Pending' ORDER BY created_at.
        // The worker uses FOR UPDATE SKIP LOCKED together with this index
        // to claim a batch without blocking parallel replicas.
        builder.HasIndex(static run => run.Status)
            .HasDatabaseName("ix_generic_command_runs_status");

        // Per-project listing surface (operator dashboard).
        builder.HasIndex(static run => run.ProjectId)
            .HasDatabaseName("ix_generic_command_runs_project");
    }
}
