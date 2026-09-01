using Comuki.Modules.Memory.Domain.Facts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>
/// memory_facts mapping. The partial unique index enforces the supersede
/// contract at the database level: at most one ACTIVE row per
/// (scope, subject_id, topic_key) — superseded rows keep their history.
/// The pgvector <c>embedding</c> column is managed outside the EF model
/// (raw SQL; see the initial migration).
/// </summary>
public sealed class MemoryFactConfiguration : IEntityTypeConfiguration<MemoryFact>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MemoryFact> builder)
    {
        _ = builder.ToTable(MemoryTables.MemoryFacts);
        _ = builder.HasKey(static fact => fact.Id);

        _ = builder.Property(static fact => fact.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.MemoryFactIdToUuid)
            .ValueGeneratedNever();

        _ = builder.Property(static fact => fact.Scope)
            .HasColumnName("scope")
            .HasConversion(MemoryKeyConverters.ScopeToKey)
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static fact => fact.SubjectId)
            .HasColumnName("subject_id")
            .HasMaxLength(128)
            .IsRequired();

        _ = builder.Property(static fact => fact.Kind)
            .HasColumnName("kind")
            .HasConversion(MemoryKeyConverters.KindToKey)
            .HasMaxLength(16)
            .IsRequired();

        _ = builder.Property(static fact => fact.TopicKey)
            .HasColumnName("topic_key")
            .HasMaxLength(256)
            .IsRequired();

        _ = builder.Property(static fact => fact.Text)
            .HasColumnName("text")
            .HasMaxLength(4000)
            .IsRequired();

        _ = builder.Property(static fact => fact.Source)
            .HasColumnName("source")
            .HasConversion(MemoryKeyConverters.SourceToKey)
            .HasMaxLength(32)
            .IsRequired();

        _ = builder.Property(static fact => fact.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(128)
            .IsRequired();

        _ = builder.Property(static fact => fact.CreatedAt)
            .HasColumnName("created_at");

        _ = builder.Property(static fact => fact.SupersededAt)
            .HasColumnName("superseded_at");

        _ = builder.HasIndex(static fact => new { fact.Scope, fact.SubjectId, fact.TopicKey })
            .IsUnique()
            .HasDatabaseName("ix_memory_facts_active_topic")
            .HasFilter("superseded_at IS NULL");

        _ = builder.HasIndex(static fact => new { fact.Scope, fact.SubjectId, fact.CreatedAt })
            .HasDatabaseName("ix_memory_facts_subject_created");
    }
}
