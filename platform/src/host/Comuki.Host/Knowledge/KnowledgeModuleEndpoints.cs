using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Application.Documents;

namespace Comuki.Host.Knowledge;

/// <summary>
/// REST surface for the knowledge layer (S10 #9 + read follow-up):
/// <list type="bullet">
///   <item><c>POST /api/v1/knowledge/ingest</c> — permission <c>knowledge:write</c>, delegates to <see cref="IKnowledgeIngestor"/>.</item>
///   <item><c>GET /api/v1/knowledge/documents</c> — permission <c>knowledge:read</c>, paged library listing via <see cref="IKnowledgeDocumentReader"/>.</item>
///   <item><c>GET /api/v1/knowledge/search</c> — permission <c>knowledge:read</c>, pgvector cosine hits via <see cref="IKnowledgeSearcher"/> (the same path the MCP <c>search_knowledge</c> tool takes; returns an empty list when pgvector is absent — graceful degradation, not an error).</item>
/// </list>
/// </summary>
public static class KnowledgeModuleEndpoints
{
    /// <summary>Default number of search hits when the caller sends no <c>topK</c>.</summary>
    public const int DefaultSearchTopK = 8;

    /// <summary>Default minimum cosine similarity when the caller sends no <c>minSimilarity</c>.</summary>
    public const float DefaultMinSimilarity = 0.2f;

    /// <summary>Maps the knowledge endpoints.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.KnowledgeIngest, IngestAsync).WithTags("Knowledge");
        app.MapGet(ApiRoutes.KnowledgeDocuments, ListDocumentsAsync).WithTags("Knowledge");
        app.MapGet(ApiRoutes.KnowledgeSearch, SearchAsync).WithTags("Knowledge");
        return app;
    }

    [RequiresPermission("knowledge:write")]
    private static async Task<IResult> IngestAsync(
        KnowledgeIngestRequest request,
        IKnowledgeIngestor ingestor,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.Problem(
                title: "Knowledge ingest body required",
                detail: "POST /api/v1/knowledge/ingest requires a JSON body with title / source / sourceRef / mimeType / text",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await ingestor.IngestAsync(
            request.ProjectId,
            request.Title,
            request.Source,
            request.SourceRef,
            request.MimeType,
            request.Text,
            cancellationToken);

        return Results.Ok(new KnowledgeIngestResponse(
            SourceDocumentId: result.SourceDocumentId.ToString(),
            ChunksWritten: result.ChunksWritten));
    }

    [RequiresPermission("knowledge:read")]
    private static async Task<IResult> ListDocumentsAsync(
        Guid? projectId,
        int? page,
        int? pageSize,
        IKnowledgeDocumentReader reader,
        CancellationToken cancellationToken)
    {
        var documents = await reader.ListAsync(projectId, page ?? 1, pageSize ?? 25, cancellationToken);
        return Results.Ok(documents);
    }

    [RequiresPermission("knowledge:read")]
    private static async Task<IResult> SearchAsync(
        HttpRequest httpRequest,
        IKnowledgeSearcher searcher,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Query.TryGetValue("q", out var queryValues) || string.IsNullOrWhiteSpace(queryValues.ToString()))
        {
            return KnowledgeReadResults.QueryRequired();
        }

        var topK = httpRequest.Query.TryGetValue("topK", out var topKValues) && int.TryParse(topKValues, out var parsedTopK)
            ? parsedTopK
            : DefaultSearchTopK;
        var minSimilarity = httpRequest.Query.TryGetValue("minSimilarity", out var minSimilarityValues)
            && float.TryParse(minSimilarityValues, provider: null, out var parsedMinSimilarity)
            ? parsedMinSimilarity
            : DefaultMinSimilarity;

        if (topK is < 1 or > 1000)
        {
            return KnowledgeReadResults.TopKOutOfRange(topK);
        }

        if (minSimilarity is < 0f or > 1f)
        {
            return KnowledgeReadResults.MinSimilarityOutOfRange(minSimilarity);
        }

        var projectId = httpRequest.Query.TryGetValue("projectId", out var projectValues)
            && Guid.TryParse(projectValues, out var parsedProjectId)
            ? parsedProjectId
            : (Guid?)null;

        var hits = await searcher.SearchAsync(queryValues.ToString(), projectId, topK, minSimilarity, cancellationToken);
        return Results.Ok(new KnowledgeSearchResponse(
            [.. hits.Select(static hit => new KnowledgeSearchHitView(
                hit.SourceDocumentId.Value,
                hit.ChunkId.Value,
                hit.ChunkText,
                hit.Similarity))]));
    }
}

/// <summary>Wire row of one search hit — document id, chunk id, snippet and cosine similarity score.</summary>
/// <param name="DocumentId">Source document the chunk belongs to.</param>
/// <param name="ChunkId">The chunk id.</param>
/// <param name="Snippet">The chunk text.</param>
/// <param name="Score">Cosine similarity in [0.0, 1.0] — higher is closer.</param>
public sealed record KnowledgeSearchHitView(
    Guid DocumentId,
    Guid ChunkId,
    string Snippet,
    float Score);

/// <summary>Search response envelope.</summary>
/// <param name="Items">Chunk hits, best first.</param>
public sealed record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchHitView> Items);

/// <summary>Problem results of the knowledge read surface (same shape as the runs surface).</summary>
internal static class KnowledgeReadResults
{
    /// <summary>400 — the <c>q</c> parameter is missing or blank.</summary>
    public static IResult QueryRequired()
    {
        return TypedResults.Problem(
            title: "Search query required",
            detail: "GET /api/v1/knowledge/search requires a non-empty 'q' query parameter",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "knowledge.query_required" });
    }

    /// <summary>400 — <c>topK</c> outside [1, 1000].</summary>
    public static IResult TopKOutOfRange(int topK)
    {
        return TypedResults.Problem(
            title: "topK out of range",
            detail: $"topK must be in [1, 1000], got {topK}",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "knowledge.top_k_out_of_range" });
    }

    /// <summary>400 — <c>minSimilarity</c> outside [0.0, 1.0].</summary>
    public static IResult MinSimilarityOutOfRange(float minSimilarity)
    {
        return TypedResults.Problem(
            title: "minSimilarity out of range",
            detail: $"minSimilarity must be in [0.0, 1.0], got {minSimilarity}",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "knowledge.min_similarity_out_of_range" });
    }
}
