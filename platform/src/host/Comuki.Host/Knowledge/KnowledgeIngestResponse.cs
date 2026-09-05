namespace Comuki.Host.Knowledge;

/// <summary>Response body for <c>POST /api/v1/knowledge/ingest</c> — the source id + chunk count.</summary>
/// <param name="SourceDocumentId">The <c>memory.source_documents</c> row the ingest produced.</param>
/// <param name="ChunksWritten">Chunks the chunker split out and persisted with embeddings.</param>
public sealed record KnowledgeIngestResponse(string SourceDocumentId, int ChunksWritten);
