using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;

/// <summary>
/// sync_jobs mapping: the ticket FK cascades inside the intake context;
/// the run id is a plain uuid value (the runs table belongs to the
/// orchestration context — no cross-context FK). Idempotency: unique
/// run_id (a run reaches its single terminal status only once).
/// </summary>
public sealed class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SyncJob> builder)
    {
        builder.ToTable(IntakeTables.SyncJobs);
        builder.HasKey(static job => job.Id);

        builder.Property(static job => job.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(static job => job.TicketId)
            .HasColumnName("ticket_id")
            .HasConversion(IntakeIdConverters.TicketIdToUuid);

        builder.HasOne<IncomingTicket>()
            .WithMany()
            .HasForeignKey(static job => job.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(static job => job.ConnectionId)
            .HasColumnName("connection_id")
            .HasConversion(IntakeIdConverters.ConnectionIdToUuid);

        builder.Property(static job => job.RunId)
            .HasColumnName("run_id")
            .HasConversion(IntakeIdConverters.RunIdToUuid);

        builder.Property(static job => job.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static job => job.ExternalUrl)
            .HasColumnName("external_url")
            .HasMaxLength(2048);

        builder.Property(static job => job.RunStatus)
            .HasColumnName("run_status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(static job => job.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static job => job.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0);

        builder.Property(static job => job.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2048);

        builder.Property(static job => job.NextAttemptAt)
            .HasColumnName("next_attempt_at");

        builder.Property(static job => job.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static job => job.UpdatedAt)
            .HasColumnName("updated_at");

        // terminal-once idempotency: one job per finished run
        builder.HasIndex(static job => job.RunId)
            .IsUnique()
            .HasDatabaseName("ux_sync_jobs_run_id");

        builder.HasIndex(static job => job.NextAttemptAt)
            .HasDatabaseName("ix_sync_jobs_due")
            .HasFilter("status = 'Pending'");
    }
}
