using Comuki.Modules.Memory.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>
/// memory_embeddings mapping. The pgvector <c>embedding</c> column is
/// deliberately absent from the EF model — created and queried through
/// raw SQL (<see cref="Stores.MemoryEmbeddingSql"/>) so the module
/// keeps the same separation as <c>memory_facts.embedding</c> and needs
/// no EF-pgvector provider.
/// </summary>
public sealed class MemoryEmbeddingConfiguration : IEntityTypeConfiguration<MemoryEmbedding>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MemoryEmbedding> builder)
    {
        builder.ToTable(MemoryDatabase.MemoryEmbeddings, MemoryDatabase.Schema);
        builder.HasKey(static embedding => embedding.Id);

        builder.Property(static embedding => embedding.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.MemoryEmbeddingIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static embedding => embedding.ProjectId)
            .HasColumnName("project_id");

        builder.Property(static embedding => embedding.SourceDocumentId)
            .HasColumnName("source_document_id")
            .HasConversion(MemoryIdConverters.SourceDocumentIdToUuid)
            .IsRequired();

        builder.Property(static embedding => embedding.ChunkIndex)
            .HasColumnName("chunk_index")
            .IsRequired();

        builder.Property(static embedding => embedding.ChunkText)
            .HasColumnName("chunk_text")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(static embedding => embedding.TokenCount)
            .HasColumnName("token_count")
            .IsRequired();

        builder.Property(static embedding => embedding.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static embedding => new { embedding.SourceDocumentId, embedding.ChunkIndex })
            .IsUnique()
            .HasDatabaseName("ix_memory_embeddings_source_chunk");

        builder.HasIndex(static embedding => embedding.ProjectId)
            .HasDatabaseName("ix_memory_embeddings_project");
    }
}
