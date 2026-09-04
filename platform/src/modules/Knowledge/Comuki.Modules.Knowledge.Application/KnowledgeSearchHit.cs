using Comuki.Modules.Memory.Domain.Knowledge;

namespace Comuki.Modules.Knowledge.Application;

/// <summary>One search hit — a chunk of a <see cref="SourceDocument"/> with its cosine similarity.</summary>
/// <param name="ChunkId">The chunk id (UUIDv7).</param>
/// <param name="SourceDocumentId">The source document the chunk belongs to.</param>
/// <param name="ChunkText">The chunk text (already trimmed when persisted).</param>
/// <param name="Similarity">Cosine similarity in [0.0, 1.0] — higher means closer.</param>
public sealed record KnowledgeSearchHit(
    MemoryEmbeddingId ChunkId,
    SourceDocumentId SourceDocumentId,
    string ChunkText,
    float Similarity);
