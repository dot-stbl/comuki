namespace Comuki.Modules.Knowledge.Application;

/// <summary>
/// Search the knowledge corpus by semantic similarity. The query is
/// embedded with the configured provider and matched against
/// <c>memory_embeddings</c> via pgvector cosine distance; null
/// <paramref name="projectId"/> widens the search to the global corpus.
/// </summary>
public interface IKnowledgeSearcher
{
    /// <summary>
    /// Find the top-K chunks whose embeddings are closest to the query
    /// embedding, filtered by minimum cosine similarity (0.0–1.0).
    /// Returns an empty list when nothing crosses the threshold or when
    /// the pgvector extension is unavailable (graceful degradation).
    /// </summary>
    /// <param name="query"></param>
    /// <param name="projectId"></param>
    /// <param name="topK"></param>
    /// <param name="minSimilarity"></param>
    /// <param name="cancellationToken"></param>
    public Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        string query,
        Guid? projectId,
        int topK,
        float minSimilarity,
        CancellationToken cancellationToken = default);
}
