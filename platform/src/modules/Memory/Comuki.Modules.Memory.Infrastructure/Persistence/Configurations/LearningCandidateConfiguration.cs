using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>learning_candidates mapping: uuid id, pattern, source ref, repeat counter, status key, decided_at.</summary>
public sealed class LearningCandidateConfiguration : IEntityTypeConfiguration<LearningCandidate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LearningCandidate> builder)
    {
        builder.ToTable(MemoryTables.LearningCandidates);
        builder.HasKey(static candidate => candidate.Id);

        builder.Property(static candidate => candidate.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.LearningCandidateIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static candidate => candidate.Pattern)
            .HasColumnName("pattern")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(static candidate => candidate.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static candidate => candidate.RepeatCount)
            .HasColumnName("repeat_count");

        builder.Property(static candidate => candidate.Status)
            .HasColumnName("status")
            .HasConversion(MemoryKeyConverters.LearningStatusToKey)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static candidate => candidate.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(static candidate => candidate.DecidedAt)
            .HasColumnName("decided_at");

        builder.HasIndex(static candidate => candidate.Status)
            .HasDatabaseName("ix_learning_candidates_status");
    }
}
