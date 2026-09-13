namespace Comuki.Modules.Knowledge.Application.Documents;

/// <summary>
/// Paged listing of the knowledge corpus documents (the library page).
/// Chunk counts and token totals are aggregated from the embedded chunks;
/// the pgvector column itself never crosses this port.
/// </summary>
public interface IKnowledgeDocumentReader
{
    /// <summary>
    /// Lists source documents newest-first with their chunk aggregates.
    /// The subject-scope query filter of the knowledge context applies —
    /// out-of-scope documents are absent from items and total.
    /// </summary>
    /// <param name="projectId">Optional project filter; null lists the global corpus only.</param>
    /// <param name="page">1-based page number (normalized to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (clamped to [1, 100]).</param>
    /// <param name="cancellationToken"></param>
    public Task<KnowledgeDocumentsPage> ListAsync(
        Guid? projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
