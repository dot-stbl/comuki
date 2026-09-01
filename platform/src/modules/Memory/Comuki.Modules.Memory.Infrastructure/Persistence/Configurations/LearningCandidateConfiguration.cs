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
        _ = builder.ToTable(MemoryTables.LearningCandidates);
        _ = builder.HasKey(static candidate => candidate.Id);

        _ = builder.Property(static candidate => candidate.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.LearningCandidateIdToUuid)
            .ValueGeneratedNever();

        _ = builder.Property(static candidate => candidate.Pattern)
            .HasColumnName("pattern")
            .HasMaxLength(2000)
            .IsRequired();

        _ = builder.Property(static candidate => candidate.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(512)
            .IsRequired();

        _ = builder.Property(static candidate => candidate.RepeatCount)
            .HasColumnName("repeat_count");

        _ = builder.Property(static candidate => candidate.Status)
            .HasColumnName("status")
            .HasConversion(MemoryKeyConverters.LearningStatusToKey)
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static candidate => candidate.CreatedAt)
            .HasColumnName("created_at");

        _ = builder.Property(static candidate => candidate.DecidedAt)
            .HasColumnName("decided_at");

        _ = builder.HasIndex(static candidate => candidate.Status)
            .HasDatabaseName("ix_learning_candidates_status");
    }
}
