using System.Text.Json;
using Comuki.Host.Runs;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
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
/// <param name="memoryStore">Memory facts store behind memory.recall / memory.note.</param>
/// <param name="noteRateLimiter">Per-worker write limiter for memory.note.</param>
/// <param name="runsList">Runs list handler (read model projection).</param>
/// <param name="clock">Clock for memory.note timestamps.</param>
/// <param name="embedder">
/// Optional embedding client — the same provider the knowledge module
/// uses. Present ⇒ memory.note embeds the fact text and memory.recall runs
/// the cosine path; absent or noop ⇒ both degrade to the embedding-free
/// fallback ranking (the memory module's documented posture).
/// </param>
public sealed class McpToolHandlers(
    IKnowledgeSearcher knowledgeSearcher,
    IKnowledgeIngestor knowledgeIngestor,
    IMemoryStore memoryStore,
    WorkerNoteRateLimiter noteRateLimiter,
    RunsListHandler runsList,
    TimeProvider clock,
    IEmbeddingClient? embedder = null)
{
    /// <summary>Default fact count for memory.recall.</summary>
    public const int RecallDefaultTopK = 5;

    /// <summary>Upper bound for memory.recall's topK.</summary>
    public const int RecallMaxTopK = 20;

    /// <summary>Upper bound for memory.note's topic — the memory_facts.topic_key column limit.</summary>
    public const int TopicKeyMaxLength = 256;

    /// <summary>Upper bound for memory.note's text — the memory_facts.text column limit.</summary>
    public const int TextMaxLength = 4000;
    /// <summary>knowledge.search — pgvector cosine similarity search. Worker callers are confined to their project.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="caller">Resolved caller of the dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> KnowledgeSearchAsync(
        JsonElement? id,
        JsonElement arguments,
        McpCaller caller,
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

        // Worker callers never scope themselves: the project comes from the
        // lease the token maps to, a client-supplied projectId is ignored.
        if (caller.Worker is { } worker)
        {
            return worker.ProjectId is not { } workerProject
                ? JsonRpcResponse.Success(id, new ToolResult(
                    Content: [new ToolContentBlock("text", "knowledge.search is unavailable: no active work item, no project scope.")],
                    IsError: true))
                : await McpKnowledgeSearch.SearchAsync(knowledgeSearcher, id, query, workerProject, arguments, cancellationToken);
        }

        var projectId = McpArgumentReaders.ReadOptionalGuid(arguments, "projectId");
        return await McpKnowledgeSearch.SearchAsync(knowledgeSearcher, id, query, projectId, arguments, cancellationToken);
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

    /// <summary>
    /// memory.recall — search the worker's project memory facts (decisions,
    /// constraints, gotchas prior workers recorded). Project scope only:
    /// the worker's lease-derived project, never a client-supplied one.
    /// </summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="caller">Resolved caller of the dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> MemoryRecallAsync(
        JsonElement? id,
        JsonElement arguments,
        McpCaller caller,
        CancellationToken cancellationToken)
    {
        if (caller.Worker is not { ProjectId: { } projectId })
        {
            return JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", "memory.recall is available only to a worker with an active work item.")],
                IsError: true));
        }

        var query = McpArgumentReaders.ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "memory.recall requires a non-empty arguments.query",
                Data: null);
        }

        var topK = Math.Clamp(McpArgumentReaders.ReadOptionalInt(arguments, "topK") ?? RecallDefaultTopK, 1, RecallMaxTopK);

        var facts = await memoryStore.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Project,
                SubjectId: projectId.ToString(),
                Embedding: await McpMemoryEmbeddings.TryEmbedAsync(embedder, query),
                Limit: topK),
            cancellationToken);

        var payload = facts.Count == 0
            ? $"no memory facts for '{query}'"
            : string.Join("\n", facts.Select(static fact =>
                $"[{MemoryFactKindKeys.Key(fact.Kind)}] {fact.TopicKey}: {fact.Text}"));

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", payload)],
            IsError: false));
    }

    /// <summary>
    /// memory.note — one durable fact into the worker's project memory
    /// (source <see cref="MemorySource.Run"/>, created_by the worker id).
    /// Same-topic writes supersede; rate-limited per worker.
    /// </summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="caller">Resolved caller of the dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> MemoryNoteAsync(
        JsonElement? id,
        JsonElement arguments,
        McpCaller caller,
        CancellationToken cancellationToken)
    {
        if (caller.Worker is not { ProjectId: { } projectId } worker)
        {
            return JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", "memory.note is available only to a worker with an active work item.")],
                IsError: true));
        }

        var topic = McpArgumentReaders.ReadString(arguments, "topic");
        var text = McpArgumentReaders.ReadString(arguments, "text");
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(text))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "memory.note requires non-empty arguments.topic and arguments.text",
                Data: null);
        }

        if (topic.Length > TopicKeyMaxLength || text.Length > TextMaxLength)
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                $"memory.note rejects arguments longer than topic {TopicKeyMaxLength} / text {TextMaxLength} characters",
                Data: null);
        }

        if (!noteRateLimiter.TryAcquire(worker.WorkerId))
        {
            return JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", $"memory.note rate limit reached ({WorkerNoteRateLimiter.Limit} per {WorkerNoteRateLimiter.Window.TotalMinutes} minutes) — write fewer, more durable facts.")],
                IsError: true));
        }

        var kind = McpArgumentReaders.ReadOptionalBool(arguments, "ephemeral") is true
            ? MemoryFactKind.Ephemeral
            : MemoryFactKind.Standing;

        var written = await memoryStore.WriteAsync(
            new MemoryFactWrite(
                Scope: MemoryScope.Project,
                SubjectId: projectId.ToString(),
                Kind: kind,
                TopicKey: topic,
                Text: text,
                Source: MemorySource.Run,
                CreatedBy: $"worker:{worker.WorkerId.Value}",
                Embedding: await McpMemoryEmbeddings.TryEmbedAsync(embedder, text),
                CreatedAt: clock.GetUtcNow()),
            cancellationToken);

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", $"remembered '{written.TopicKey}' ({MemoryFactKindKeys.Key(written.Kind)})")],
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

/// <summary>
/// The shared body of knowledge.search: pgvector cosine search over the
/// resolved project scope, rendered as the JSON tool payload. Both caller
/// kinds (subject with its own project argument, worker with the forced
/// lease project) end here.
/// </summary>
file static class McpKnowledgeSearch
{
    public static async Task<JsonRpcResponse> SearchAsync(
        IKnowledgeSearcher knowledgeSearcher,
        JsonElement? id,
        string query,
        Guid? projectId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
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
}

/// <summary>
/// The embedding seam of the worker memory tools — the same posture the
/// brain toolbox takes: embed when a real model is configured, degrade
/// silently otherwise (a noop provider's junk vectors would cosine-rank
/// arbitrarily, so the fallback ranking is the better answer there).
/// </summary>
file static class McpMemoryEmbeddings
{
    public static async Task<float[]?> TryEmbedAsync(IEmbeddingClient? embedder, string text)
    {
        if (embedder is null || embedder.ProviderName == EmbeddingProviderKindKeys.Noop)
        {
            return null;
        }

        try
        {
            return await embedder.EmbedAsync(text);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // transport / auth / provider-rejected — same degradation as
            // "not configured": memory must keep working without embeddings
            return null;
        }
    }
}
