namespace Comuki.Modules.Knowledge.Application;

/// <summary>
/// Ingest one source of knowledge content — split into chunks,
/// embed each chunk, write one <c>memory_embeddings</c> row per chunk.
/// Project-scoped (projectId set) or global (projectId null). The
/// ingestion is idempotent at the <see cref="SourceDocument"/> level:
/// re-ingesting the same (projectId, source, sourceRef) supersedes the
/// previous row and replaces its chunks.
/// </summary>
public interface IKnowledgeIngestor
{
    /// <summary>
    /// Ingest one source document; returns the <see cref="SourceDocumentId"/>
    /// it was written under and the chunk count produced.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="title"></param>
    /// <param name="source">Wire key (git | upload | url).</param>
    /// <param name="sourceRef"></param>
    /// <param name="mimeType"></param>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ArgumentException">A required string is empty.</exception>
    /// <exception cref="InvalidOperationException">Embedding provider unavailable or chunking failed.</exception>
    public Task<KnowledgeIngestResult> IngestAsync(
        Guid? projectId,
        string title,
        string source,
        string sourceRef,
        string mimeType,
        string text,
        CancellationToken cancellationToken = default);
}
