using Comuki.Modules.Work.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Work.Infrastructure.Persistence.Configurations;

/// <summary>WorkTaskAttempts mapping: append-only ledger keyed on (task_id, attempt_ordinal).</summary>
public sealed class WorkTaskAttemptConfiguration : IEntityTypeConfiguration<WorkTaskAttemptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkTaskAttemptEntity> builder)
    {
        builder.ToTable(WorkDatabase.WorkTaskAttempts, WorkDatabase.Schema);
        builder.HasKey(static attempt => attempt.Id);

        builder.Property(static attempt => attempt.Id)
            .HasColumnName("id");

        builder.Property(static attempt => attempt.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(static attempt => attempt.AttemptOrdinal)
            .HasColumnName("attempt_ordinal")
            .IsRequired();

        builder.Property(static attempt => attempt.RunId)
            .HasColumnName("run_id")
            .IsRequired();

        builder.Property(static attempt => attempt.TerminalStatus)
            .HasColumnName("terminal_status")
            .HasMaxLength(16);

        builder.Property(static attempt => attempt.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(static attempt => attempt.TerminalAt)
            .HasColumnName("terminal_at");

        // The attempt-ledger mirror: uniqueness on (task_id, attempt_ordinal)
        // is the WorkTask one-active-Run invariant's database-side
        // guarantee. Concurrent double-AppendAttempt on the same ordinal
        // lands on this index and the second insert fails — the Work
        // inbox surfaces as 409 per task 2.7.
        builder.HasIndex(static attempt => new { attempt.TaskId, attempt.AttemptOrdinal })
            .IsUnique()
            .HasDatabaseName("ux_work_task_attempts_ordinal");

        // Engine-side Run → Work attempt join (the IngestRunTerminal
        // subscriber looks up the attempt by run_id to advance the Task).
        builder.HasIndex(static attempt => attempt.RunId)
            .HasDatabaseName("ix_work_task_attempts_run_id");
    }
}
