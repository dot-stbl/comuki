using System.Text.Json;
using Comuki.Host.Runs;
using Comuki.Modules.Knowledge.Application;
using Comuki.Shared.Filtering.Ports;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-tool handlers for the MCP JSON-RPC <c>tools/call</c> dispatcher.
/// Extracted from <c>McpServer</c> so the dispatcher class holds only
/// orchestration (per <c>class-layout-and-tooling.md §1a</c>). Each
/// handler is a pure function over its inputs and the injected ports —
/// no instance state, no side effects beyond the port calls.
/// </summary>
/// <param name="knowledgeSearcher">Knowledge base search port.</param>
/// <param name="knowledgeIngestor">Knowledge ingestion port.</param>
/// <param name="runsList">Runs list handler (read model projection).</param>
public sealed class McpToolHandlers(
    IKnowledgeSearcher knowledgeSearcher,
    IKnowledgeIngestor knowledgeIngestor,
    RunsListHandler runsList)
{
    /// <summary>knowledge.search — pgvector cosine similarity search.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> KnowledgeSearchAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var query = McpArgumentReaders.ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "knowledge.search requires a non-empty arguments.query",
                Data: null);
        }

        var projectId = McpArgumentReaders.ReadOptionalGuid(arguments, "projectId");
        var topK = McpArgumentReaders.ReadOptionalInt(arguments, "topK") ?? 5;
        var minSimilarity = McpArgumentReaders.ReadOptionalFloat(arguments, "minSimilarity") ?? 0.5f;

        var hits = await knowledgeSearcher.SearchAsync(query, projectId, topK, minSimilarity, cancellationToken);
        var payload = hits.Select(static hit => new
        {
            chunkId = hit.ChunkId.ToString(),
            sourceDocumentId = hit.SourceDocumentId.ToString(),
            similarity = hit.Similarity,
            chunkText = hit.ChunkText,
        }).ToArray();

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(payload, JsonSerializerOptions.Web))],
            IsError: false));
    }

    /// <summary>knowledge.ingest — chunked + embedded ingestion into pgvector.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> KnowledgeIngestAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var title = McpArgumentReaders.ReadString(arguments, "title");
        var source = McpArgumentReaders.ReadString(arguments, "source");
        var sourceRef = McpArgumentReaders.ReadString(arguments, "sourceRef");
        var mimeType = McpArgumentReaders.ReadString(arguments, "mimeType");
        var text = McpArgumentReaders.ReadString(arguments, "text");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(mimeType) || string.IsNullOrWhiteSpace(text))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "knowledge.ingest requires arguments.title, source, sourceRef, mimeType, text",
                Data: null);
        }

        var projectId = McpArgumentReaders.ReadOptionalGuid(arguments, "projectId");
        var result = await knowledgeIngestor.IngestAsync(projectId, title, source, sourceRef, mimeType, text, cancellationToken);

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(new
            {
                sourceDocumentId = result.SourceDocumentId.ToString(),
                chunksWritten = result.ChunksWritten,
            }, JsonSerializerOptions.Web))],
            IsError: false));
    }

    /// <summary>runs.list — paginated runs, optional project + status filter.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> RunsListAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var projectId = McpArgumentReaders.ReadOptionalGuid(arguments, "projectId");
        var status = McpArgumentReaders.ReadOptionalString(arguments, "status");

        var clauses = new List<string>();
        if (projectId is { } projectValue)
        {
            clauses.Add($"projectId=={projectValue:D}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            clauses.Add($"status=={status}");
        }

        var query = new FilterQuery { Filter = clauses.Count > 0 ? string.Join(';', clauses) : null };

        var page = await runsList.ListAsync(query, cancellationToken);
        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(page, JsonSerializerOptions.Web))],
            IsError: false));
    }

    /// <summary>runs.get — fetch one run by id. v1 returns a "not yet implemented" tool error.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    public static Task<JsonRpcResponse> RunsGetAsync(JsonElement? id, JsonElement arguments)
    {
        // The existing runs surface exposes list + decision endpoints;
        // a single-run get-by-id helper lands in a later slice alongside
        // a dedicated GET /api/v1/runs/{id}. Until then surface the
        // gap as a tool error (vs. a JSON-RPC error) so the caller can
        // see the feature is not yet implemented.
        var runIdText = McpArgumentReaders.ReadString(arguments, "runId");
        return Guid.TryParse(runIdText, out _)
            ? Task.FromResult(JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", $"runs.get for {runIdText} is not yet implemented; use runs.list and filter by runId, or wait for a follow-up slice.")],
                IsError: true)))
            : Task.FromResult(JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "runs.get requires arguments.runId as a Guid string",
                Data: null));
    }
}
