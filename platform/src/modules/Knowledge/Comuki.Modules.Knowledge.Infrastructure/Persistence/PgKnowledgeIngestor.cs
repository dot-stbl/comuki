using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Chunking;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Memory.Domain.Knowledge;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// pgvector-backed knowledge ingestor. Splits the input text via
/// <see cref="Chunker"/>, writes one
/// <see cref="MemoryEmbedding"/> row per chunk into the
/// <c>memory_embeddings</c> table, then back-fills the pgvector
/// <c>embedding</c> column with raw SQL. The insert + UPDATE pair runs
/// inside the DbContext's open transaction (caller's responsibility —
/// <see cref="IKnowledgeIngestor.IngestAsync"/> opens its own scope via
/// the <see cref="IDbContextFactory{T}"/>), so a partial failure
/// surfaces to the caller as an exception with no half-written rows.
/// </summary>
public sealed class PgKnowledgeIngestor(
    IDbContextFactory<MemoryDbContext> contextFactory,
    IEmbeddingClient embedder,
    IOptions<KnowledgeIngestOptions> ingestOptions,
    TimeProvider clock,
    ILogger<PgKnowledgeIngestor> logger) : IKnowledgeIngestor
{
    /// <inheritdoc />
    public async Task<KnowledgeIngestResult> IngestAsync(
        Guid? projectId,
        string title,
        string source,
        string sourceRef,
        string mimeType,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var sourceKind = SourceKindKeys.ParseRequired(source);
        var targetTokens = ingestOptions.Value.ChunkTokenTarget;
        var now = clock.GetUtcNow();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var document = SourceDocument.Create(projectId, title, sourceKind, sourceRef, mimeType, now);
        context.SourceDocuments.Add(document);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var chunks = Chunker.Chunk(text, targetTokens);
        if (chunks.Count == 0)
        {
            // No chunks produced — commit the document row so the
            // operator sees a record of the attempt, but write zero
            // embeddings. The caller can decide what to do with this
            // (404 vs 200 with chunksWritten = 0).
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "knowledge ingest wrote an empty document {DocumentId} for {SourceRef} — no chunks produced",
                document.Id,
                sourceRef);
            return new KnowledgeIngestResult(document.Id, ChunksWritten: 0);
        }

        var vectors = await embedder.EmbedBatchAsync(chunks, cancellationToken).ConfigureAwait(false);
        if (vectors.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"embedding provider returned {vectors.Count} vectors for {chunks.Count} chunks — aborting ingest");
        }

        var rows = new List<MemoryEmbedding>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            var tokenCount = Chunker.EstimateTokens(chunk);
            rows.Add(MemoryEmbedding.Create(projectId, document.Id, index, chunk, tokenCount, now));
        }

        context.MemoryEmbeddings.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Back-fill the pgvector embedding column per row via raw SQL —
        // the EF model deliberately doesn't model the vector type.
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = MemoryEmbeddingSql.UpdateEmbeddingSql;
            update.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            var idParameter = update.Parameters.Add("@id", NpgsqlTypes.NpgsqlDbType.Uuid);
            var vectorParameter = update.Parameters.Add("@vector", NpgsqlTypes.NpgsqlDbType.Text);
            foreach (var pair in rows.Zip(vectors, static (row, vector) => (row, vector)))
            {
                idParameter.Value = pair.row.Id.Value;
                vectorParameter.Value = MemoryEmbeddingSql.VectorLiteral(pair.vector);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "knowledge ingest wrote {ChunkCount} chunks for document {DocumentId} ({SourceKind} {SourceRef})",
            rows.Count,
            document.Id,
            sourceKind,
            sourceRef);

        return new KnowledgeIngestResult(document.Id, ChunksWritten: rows.Count);
    }
}
