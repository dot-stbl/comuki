using Comuki.Modules.Knowledge.Application.Documents;
using Comuki.Modules.Knowledge.Domain;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// EF implementation of <see cref="IKnowledgeDocumentReader"/>: newest-first
/// document listing with chunk aggregates joined from
/// <c>memory_embeddings</c>. The subject-scope query filter of
/// <see cref="KnowledgeDbContext"/> applies to both halves — out-of-scope
/// documents and chunks are invisible.
/// </summary>
/// <param name="contextFactory"></param>
public sealed class PgKnowledgeDocumentReader(
    IDbContextFactory<KnowledgeDbContext> contextFactory) : IKnowledgeDocumentReader
{
    /// <inheritdoc />
    public async Task<KnowledgeDocumentsPage> ListAsync(
        Guid? projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedSize) = KnowledgeDocumentsPaging.Normalize(page, pageSize);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var documents = context.SourceDocuments.AsNoTracking();
        if (projectId is { } projectValue)
        {
            documents = documents.Where(document => document.ProjectId == projectValue);
        }

        var total = await documents.CountAsync(cancellationToken);

        var items = await documents
            .OrderByDescending(document => document.CreatedAt)
            .ThenByDescending(document => document.Id)
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .Select(document => new
            {
                document.Id,
                document.ProjectId,
                document.Title,
                document.Source,
                document.SourceRef,
                document.MimeType,
                document.CreatedAt,
                ChunkCount = context.MemoryEmbeddings
                    .Count(embedding => embedding.SourceDocumentId == document.Id),
                TokenCount = context.MemoryEmbeddings
                    .Where(embedding => embedding.SourceDocumentId == document.Id)
                    .Sum(embedding => (long?)embedding.TokenCount) ?? 0,
            })
            .ToListAsync(cancellationToken);

        return new KnowledgeDocumentsPage(
            [.. items.Select(document => new KnowledgeDocumentSummary(
                document.Id.Value,
                document.ProjectId,
                document.Title,
                SourceKindKeys.Key(document.Source),
                document.SourceRef,
                document.MimeType,
                document.ChunkCount,
                document.TokenCount,
                document.CreatedAt))],
            normalizedPage,
            normalizedSize,
            total);
    }
}
