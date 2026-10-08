using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Chunking;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
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
/// <c>knowledge.memory_embeddings</c> table, then back-fills the
/// pgvector <c>embedding</c> column with raw SQL. The insert + UPDATE
/// pair runs inside the DbContext's open transaction (caller's
/// responsibility — <see cref="IKnowledgeIngestor.IngestAsync"/> opens
/// its own scope via the <see cref="IDbContextFactory{T}"/>), so a
/// partial failure surfaces to the caller as an exception with no
/// half-written rows. The write side has no EF query filter to lean on
/// either (inserts are never filtered by <c>HasQueryFilter</c>, even for
/// the entities it does model) — <see cref="PgKnowledgeIngestorHelpers.IsProjectWritable"/> is the
/// only thing standing between a caller and another project's corpus.
///
/// Wiki ingest (<see cref="SourceKind.Wiki"/>) reuses the same
/// <c>knowledge.ingest</c> REST/MCP surface as every other kind. The
/// ingestor extracts the Wiki-specific metadata from the body's
/// frontmatter (<c>kind</c>, <c>link_graph</c>) and attaches it to the
/// <see cref="SourceDocument"/> before saving.
/// </summary>
/// <param name="contextFactory"></param>
/// <param name="embedder"></param>
/// <param name="ingestOptions"></param>
/// <param name="scopeAccessor">
/// The ambient caller scope. Required, not optional: like
/// <see cref="PgKnowledgeSearcher"/>, this class is only ever resolved
/// through the host DI container.
/// </param>
/// <param name="clock"></param>
/// <param name="logger"></param>
public sealed class PgKnowledgeIngestor(
    IDbContextFactory<KnowledgeDbContext> contextFactory,
    IEmbeddingClient embedder,
    IOptions<KnowledgeIngestOptions> ingestOptions,
    ISubjectScopeAccessor scopeAccessor,
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
        KnowledgeIngestGuards.RequireField(title, "title");
        KnowledgeIngestGuards.RequireField(source, "source");
        KnowledgeIngestGuards.RequireField(sourceRef, "sourceRef");
        KnowledgeIngestGuards.RequireField(mimeType, "mimeType");
        KnowledgeIngestGuards.RequireField(text, "text");

        var sourceKind = SourceKindKeys.ParseRequired(source);

        if (!PgKnowledgeIngestorHelpers.IsProjectWritable(scopeAccessor.Current, projectId))
        {
            // Unlike a read, a write-side refusal is safe to make loud:
            // there is no "does the project exist" question to avoid
            // answering, only "is this subject allowed to write here" —
            // so this maps to 403, not a silent no-op.
            throw new ProviderForbiddenException(
                code: "knowledge.project_out_of_scope",
                message: projectId is { } target
                    ? $"the current subject may not ingest knowledge into project '{target}'"
                    : "the current subject may not ingest a global (cross-project) knowledge document");
        }

        var targetTokens = ingestOptions.Value.ChunkTokenTarget;
        var now = clock.GetUtcNow();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var document = SourceDocument.Create(projectId, title, sourceKind, sourceRef, mimeType, now);

        if (sourceKind == SourceKind.Wiki)
        {
            PgKnowledgeIngestorHelpers.AttachWikiMetadataFromBody(document, text);
        }

        context.SourceDocuments.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        // Wiki frontmatter is metadata, not body — strip it so the
        // chunker sees the markdown content only. Other kinds carry no
        // frontmatter, so the strip is a no-op.
        var chunkableText = sourceKind == SourceKind.Wiki
            ? WikiFrontmatter.StripFrontmatter(text)
            : text;

        var chunks = Chunker.Chunk(chunkableText, targetTokens);
        if (chunks.Count == 0)
        {
            // No chunks produced — commit the document row so the
            // operator sees a record of the attempt, but write zero
            // embeddings. The caller can decide what to do with this
            // (404 vs 200 with chunksWritten = 0).
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "knowledge ingest wrote an empty document {DocumentId} for {SourceRef} — no chunks produced",
                document.Id,
                sourceRef);
            return new KnowledgeIngestResult(document.Id, ChunksWritten: 0);
        }

        var vectors = await embedder.EmbedBatchAsync(chunks, cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);

        // Back-fill the pgvector embedding column per row via raw SQL —
        // the EF model deliberately doesn't model the vector type. The
        // same availability probe used by the searcher gates this UPDATE
        // so a plain Postgres image (no pgvector extension / no
        // embedding column) skips the vector write cleanly instead of
        // throwing "type vector does not exist" mid-transaction.
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var pgvectorAvailable = await PgKnowledgeIngestorHelpers.ProbePgvectorAsync(connection, cancellationToken);

        if (pgvectorAvailable)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = EmbeddingSql.UpdateEmbeddingSql;
            update.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            var idParameter = update.Parameters.Add("@id", NpgsqlTypes.NpgsqlDbType.Uuid);
            var vectorParameter = update.Parameters.Add("@vector", NpgsqlTypes.NpgsqlDbType.Text);
            foreach (var pair in rows.Zip(vectors, static (row, vector) => (row, vector)))
            {
                idParameter.Value = pair.row.Id.Value;
                vectorParameter.Value = EmbeddingSql.VectorLiteral(pair.vector);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        else
        {
            logger.LogInformation(
                "knowledge ingest wrote {ChunkCount} chunks for document {DocumentId} — pgvector embedding column absent, vectors not persisted",
                rows.Count,
                document.Id);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "knowledge ingest wrote {ChunkCount} chunks for document {DocumentId} ({SourceKind} {SourceRef})",
            rows.Count,
            document.Id,
            sourceKind,
            sourceRef);

        return new KnowledgeIngestResult(document.Id, ChunksWritten: rows.Count);
    }
}

/// <summary>
/// Pure-function helpers for <see cref="PgKnowledgeIngestor"/>: Wiki
/// frontmatter stitching, pgvector availability probe, project write
/// guard. Extracted per the no-private-methods rule.
/// </summary>
file static class PgKnowledgeIngestorHelpers
{
    /// <summary>
    /// Pulls the Wiki frontmatter from <paramref name="body"/>, generates a
    /// fresh <see cref="WikiPageId"/>, and attaches the parsed kind and
    /// link graph to the document. EF persists the link graph as an owned
    /// jsonb projection on <see cref="SourceDocument.LinkGraph"/>; no
    /// manual serialisation. When the body carries no frontmatter (or no
    /// recognised keys) the document still gets a fresh page id —
    /// <c>kind</c> is null, the link graph is empty.
    /// </summary>
    /// <param name="document">A newly-created <see cref="SourceDocument"/> with <see cref="SourceKind.Wiki"/>.</param>
    /// <param name="body">Markdown body the caller handed to the ingestor.</param>
    public static void AttachWikiMetadataFromBody(SourceDocument document, string body)
    {
        var metadata = WikiFrontmatter.ExtractMetadata(body) ?? new WikiIngestMetadata(null, []);
        var wikiPageId = WikiPageId.New();

        document.AttachWikiMetadata(
            wikiPageId,
            metadata.Kind,
            updatedByMissionId: null,
            metadata.LinkGraph);
    }

    /// <summary>
    /// Mirrors <see cref="PgKnowledgeSearcher"/>'s availability probe —
    /// the embedding column is conditional on the pgvector extension,
    /// so a plain Postgres deployment surfaces this as <c>false</c> and
    /// the caller skips the vector UPDATE.
    /// </summary>
    public static async Task<bool> ProbePgvectorAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = EmbeddingSql.EmbeddingColumnExistsSql;
        var result = await probe.ExecuteScalarAsync(cancellationToken);
        return result is bool available && available;
    }

    /// <summary>
    /// Whether the current scope may ingest into <paramref name="projectId"/>.
    /// An unrestricted caller (platform-scope role, or a system consumer)
    /// may write anywhere, including the global corpus. A restricted
    /// caller may only write into a project it is assigned to; it may
    /// never write the global corpus (<paramref name="projectId"/> null) —
    /// that corpus is visible to every subject platform-wide, so writing
    /// it is reserved for an unrestricted caller, the same way reading it
    /// is unconditional in <c>KnowledgeDbContext</c>'s query filter.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="projectId"></param>
    public static bool IsProjectWritable(SubjectScope scope, Guid? projectId)
    {
        return scope.Unrestricted
            || (projectId is { } target && scope.ProjectIds.Contains(new ProjectId(target)));
    }
}

/// <summary>
/// Input-contract guards for <see cref="PgKnowledgeIngestor.IngestAsync"/> —
/// one validator replacing the five copy-pasted whitespace blocks, keeping
/// the no-DB-touched-before-validation guarantee the unit suite asserts.
/// </summary>
file static class KnowledgeIngestGuards
{
    /// <summary>Throws when a required ingest string is null / empty / whitespace.</summary>
    /// <param name="value">The ingest field value.</param>
    /// <param name="fieldName">Wire name of the field — carried into the exception message.</param>
    public static void RequireField(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} required");
        }
    }
}
