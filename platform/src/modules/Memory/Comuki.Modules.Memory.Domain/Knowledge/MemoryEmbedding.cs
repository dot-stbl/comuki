namespace Comuki.Modules.Memory.Domain.Knowledge;

/// <summary>
/// One embedded chunk of a <see cref="SourceDocument"/>. The
/// <c>embedding</c> pgvector column is intentionally absent from this
/// entity: it is created and queried through raw SQL
/// (<see cref="Memory.Infrastructure.Persistence.Stores.MemoryEmbeddingSql"/>),
/// same separation as <c>memory_facts.embedding</c>. The chunk text and
/// the source pointer stay in .NET — those are what the search result
/// surfaces to the caller; the vector itself is write-once, query-only.
/// </summary>
public sealed class MemoryEmbedding
{
    internal MemoryEmbedding()
    {
    }

    /// <summary>Chunk id (UUIDv7, client-side).</summary>
    public MemoryEmbeddingId Id { get; private set; }

    /// <summary>Owning project; null means the chunk belongs to a global source.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The source document this chunk was cut from.</summary>
    public SourceDocumentId SourceDocumentId { get; private set; }

    /// <summary>Zero-based chunk index inside the source document.</summary>
    public int ChunkIndex { get; private set; }

    /// <summary>The chunk text — what the embedding vector represents.</summary>
    public string ChunkText { get; private set; } = string.Empty;

    /// <summary>Approximate token count of <see cref="ChunkText"/> (used for diagnostics + budget).</summary>
    public int TokenCount { get; private set; }

    /// <summary>When the chunk was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a chunk row. The embedding vector is written separately
    /// after the row is saved (raw-SQL UPDATE) — the entity never holds
    /// the vector. Token count is computed by the chunker and accepted
    /// as-is; the entity does not re-tokenize.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="sourceDocumentId"></param>
    /// <param name="chunkIndex"></param>
    /// <param name="chunkText"></param>
    /// <param name="tokenCount"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException">The chunk text is empty.</exception>
    public static MemoryEmbedding Create(
        Guid? projectId,
        SourceDocumentId sourceDocumentId,
        int chunkIndex,
        string chunkText,
        int tokenCount,
        DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(chunkText)
            ? throw new ArgumentException("chunk text must not be empty", nameof(chunkText))
            : chunkIndex < 0
            ? throw new ArgumentOutOfRangeException(nameof(chunkIndex), chunkIndex, "chunk index must be non-negative")
            : tokenCount < 0
            ? throw new ArgumentOutOfRangeException(nameof(tokenCount), tokenCount, "token count must be non-negative")
            : new MemoryEmbedding
            {
                Id = MemoryEmbeddingId.New(),
                ProjectId = projectId,
                SourceDocumentId = sourceDocumentId,
                ChunkIndex = chunkIndex,
                ChunkText = chunkText.Trim(),
                TokenCount = tokenCount,
                CreatedAt = now,
            };
    }
}
