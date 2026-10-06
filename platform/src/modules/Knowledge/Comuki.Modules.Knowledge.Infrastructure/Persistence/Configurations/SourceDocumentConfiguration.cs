using Comuki.Modules.Knowledge.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence.Configurations;

/// <summary>
/// source_documents mapping. One row per registered corpus pointer;
/// the doc worker reads bytes through <see cref="SourceKind"/>-specific
/// loaders and writes one <c>memory_embeddings</c> row per chunk.
///
/// Wiki pages (<see cref="SourceKind.Wiki"/>) carry the four extra columns
/// under <c>wiki_*</c> / <c>link_graph</c> — populated by the ingestor when
/// the body carries the matching frontmatter subset. The columns are
/// nullable on the row (the EF model owns the shape; the domain enforces
/// "wiki columns are only set on Wiki documents" at the factory level).
/// The <c>link_graph</c> jsonb column is a projected view of the
/// <see cref="SourceDocument.LinkGraph"/> owned collection — no twin
/// string column to keep in sync.
/// </summary>
public sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable(KnowledgeDatabase.SourceDocuments, KnowledgeDatabase.Schema);
        builder.HasKey(static document => document.Id);

        builder.Property(static document => document.Id)
            .HasColumnName("id")
            .HasConversion(KnowledgeIdConverters.SourceDocumentIdToUuid)
            .ValueGeneratedNever();

        builder.Property(static document => document.ProjectId)
            .HasColumnName("project_id");

        builder.Property(static document => document.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(static document => document.Source)
            .HasColumnName("source")
            .HasConversion(KnowledgeKeyConverters.SourceKindToKey)
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

        builder.Property(static document => document.WikiPageId)
            .HasColumnName("wiki_page_id")
            .HasConversion(KnowledgeIdConverters.NullableWikiPageIdToUuid);

        builder.Property(static document => document.WikiPageKind)
            .HasColumnName("wiki_page_kind")
            .HasConversion(KnowledgeKeyConverters.NullableWikiPageKindToKey)
            .HasMaxLength(32);

        builder.Property(static document => document.UpdatedByMissionId)
            .HasColumnName("updated_by_mission_id");

        builder.OwnsMany(static document => document.LinkGraph, static link =>
        {
            link.ToJson("link_graph");
            link.Property(static l => l.TargetPageId)
                .HasJsonPropertyName("target")
                .HasConversion(KnowledgeIdConverters.WikiPageIdToUuid);
            link.Property(static l => l.Kind)
                .HasJsonPropertyName("kind")
                .HasConversion(KnowledgeKeyConverters.WikiPageLinkKindToKey);
        });

        builder.HasIndex(static document => new { document.ProjectId, document.CreatedAt })
            .HasDatabaseName("ix_source_documents_project_created");

        builder.HasIndex(static document => document.WikiPageId)
            .HasDatabaseName("ix_source_documents_wiki_page_id");
    }
}
