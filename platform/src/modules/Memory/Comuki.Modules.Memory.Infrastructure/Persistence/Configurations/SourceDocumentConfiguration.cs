using Comuki.Modules.Memory.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Configurations;

/// <summary>
/// source_documents mapping. One row per registered corpus pointer;
/// the doc worker reads bytes through <see cref="SourceKind"/>-specific
/// loaders and writes one <c>memory_embeddings</c> row per chunk.
/// </summary>
public sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable(MemoryDatabase.SourceDocuments, MemoryDatabase.Schema);
        builder.HasKey(static document => document.Id);

        builder.Property(static document => document.Id)
            .HasColumnName("id")
            .HasConversion(MemoryIdConverters.SourceDocumentIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static document => document.ProjectId)
            .HasColumnName("project_id");

        builder.Property(static document => document.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static document => document.Source)
            .HasColumnName("source")
            .HasConversion(MemoryKeyConverters.SourceKindToKey)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(static document => document.SourceRef)
            .HasColumnName("source_ref")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(static document => document.MimeType)
            .HasColumnName("mime_type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(static document => document.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(static document => new { document.ProjectId, document.CreatedAt })
            .HasDatabaseName("ix_source_documents_project_created");
    }
}
