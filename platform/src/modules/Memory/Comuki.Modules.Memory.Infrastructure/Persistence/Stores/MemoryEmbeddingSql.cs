using System.Data;
using System.Globalization;
using Comuki.Modules.Memory.Domain.Knowledge;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// Raw-SQL surface for the pgvector <c>embedding</c> column on
/// <c>memory_embeddings</c>: literal formatting, the availability probe,
/// the embedding UPDATE and the cosine-distance SELECT. The column lives
/// outside the EF model on purpose — no EF-pgvector provider, no vector
/// materialized in .NET. All SQL references
/// <see cref="MemoryDatabase.Schema"/> so the queries find the table
/// regardless of <c>search_path</c>.
/// </summary>
public static class MemoryEmbeddingSql
{
    /// <summary>Embedding vector dimension — OpenAI text-embedding-3-small default.</summary>
    public const int Dimensions = 1536;

    /// <summary>
    /// Availability probe: does the pgvector extension AND the
    /// <c>memory_embeddings.embedding</c> column both exist (graceful
    /// probe — the column is conditional in the migration).
    /// </summary>
    public const string EmbeddingColumnExistsSql =
        "SELECT EXISTS (SELECT 1 FROM information_schema.columns "
        + "WHERE table_schema = '" + MemoryDatabase.Schema + "' "
        + "AND table_name = '" + MemoryDatabase.MemoryEmbeddings + "' AND column_name = 'embedding')";

    /// <summary>Writes the embedding of one chunk row (inside the write transaction).</summary>
    public const string UpdateEmbeddingSql =
        "UPDATE " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryEmbeddings
        + " SET embedding = @vector::vector WHERE id = @id";

    /// <summary>
    /// Cosine-distance search over embedded, visible chunks; NULL filter
    /// parameters widen the scope (they must arrive text-typed — an
    /// untyped NULL parameter fails with 42P08). The vector parameter
    /// carries an untyped literal typed by the explicit <c>::vector</c>
    /// cast. <paramref name="minSimilarity"/> converts to a maximum
    /// cosine-distance bound (<c>1 - similarity</c>).
    /// </summary>
    public const string CosineSearchSql =
        "SELECT id, source_document_id, chunk_index, chunk_text, token_count, created_at, "
        + "       (1 - (embedding <=> @vector::vector)) AS similarity "
        + "FROM " + MemoryDatabase.Schema + "." + MemoryDatabase.MemoryEmbeddings + " "
        + "WHERE embedding IS NOT NULL "
        + "  AND (@projectId IS NULL OR project_id = @projectId::uuid) "
        + "  AND (1 - (embedding <=> @vector::vector)) >= @minSimilarity "
        + "ORDER BY embedding <=> @vector::vector "
        + "LIMIT @limit";

    /// <summary>Formats a vector as a pgvector literal (<c>[1,0.5,…]</c>, invariant, round-trippable).</summary>
    /// <param name="vector"></param>
    public static string VectorLiteral(float[] vector)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{string.Join(",", vector.Select(static component => component.ToString("R", CultureInfo.InvariantCulture)))}]");
    }

    /// <summary>
    /// Reads one chunk + similarity score from the current row of a
    /// cosine search. The id column is mapped back to
    /// <see cref="MemoryEmbeddingId"/>; the similarity score carries the
    /// computed cosine similarity (1 − distance).
    /// </summary>
    /// <param name="reader"></param>
    public static (MemoryEmbeddingId Id, SourceDocumentId SourceDocumentId, int ChunkIndex, string ChunkText, int TokenCount, DateTimeOffset CreatedAt, float Similarity) ReadRow(System.Data.Common.DbDataReader reader)
    {
        return (
            Id: new MemoryEmbeddingId(reader.GetGuid(0)),
            SourceDocumentId: new SourceDocumentId(reader.GetGuid(1)),
            ChunkIndex: reader.GetInt32(2),
            ChunkText: reader.GetString(3),
            TokenCount: reader.GetInt32(4),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(5),
            Similarity: reader.GetFloat(6));
    }
}
