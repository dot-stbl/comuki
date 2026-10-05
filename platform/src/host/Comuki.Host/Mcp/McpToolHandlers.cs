using System.Text.Json;
using Comuki.Host.Runs;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Observability.Application.Ports;
using Comuki.Modules.Observability.Domain;
using Comuki.Modules.Observability.Domain.Logs;
using Comuki.Modules.Observability.Domain.Metrics;
using Comuki.Shared.Filtering.Ports;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-tool handlers for the MCP JSON-RPC <c>tools/call</c> dispatcher.
/// Each handler is a pure function over its inputs and the injected
/// ports — no instance state, no side effects beyond the port calls.
/// The observability handlers parse ISO 8601 timestamps from the JSON
/// arguments at the validation clamp (per
/// <c>specs/observability/spec.md</c> "accepts
/// <c>{... from?: iso8601, to?: iso8601, ...}</c>"): the typed
/// infrastructure client converts to unix-seconds (Prometheus) or
/// RFC3339 (LogsQL <c>time</c> pipe) on the wire.
/// </summary>
/// <param name="knowledgeSearcher">Knowledge base search port.</param>
/// <param name="knowledgeIngestor">Knowledge ingestion port.</param>
/// <param name="memoryStore">Memory facts store behind memory.recall / memory.note.</param>
/// <param name="noteRateLimiter">Per-worker write limiter for memory.note.</param>
/// <param name="learningCandidates">Learning-candidate store behind learning.suggest.</param>
/// <param name="suggestRateLimiter">Per-worker suggest limiter for learning.suggest.</param>
/// <param name="runsList">Runs list handler (read model projection).</param>
/// <param name="logsQueryClient">VictoriaLogs query port (logs.search / logs.context).</param>
/// <param name="metricsQueryClient">VictoriaMetrics query port (metrics.query / metrics.series).</param>
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
    ILearningCandidateStore learningCandidates,
    WorkerSuggestRateLimiter suggestRateLimiter,
    RunsListHandler runsList,
    IVictoriaLogsQueryClient logsQueryClient,
    IVictoriaMetricsQueryClient metricsQueryClient,
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

    /// <summary>Upper bound for learning.suggest's topic — the learning_candidates.topic column limit.</summary>
    public const int SuggestTopicMaxLength = 200;

    /// <summary>Upper bound for learning.suggest's observation — the learning_candidates.observation column limit.</summary>
    public const int SuggestObservationMaxLength = 2000;

    /// <summary>Upper bound for learning.suggest's proposedRule — the learning_candidates.proposed_rule column limit.</summary>
    public const int SuggestRuleMaxLength = 2000;

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
        return caller.Worker is { } worker
            ? worker.ProjectId is not { } workerProject
                ? JsonRpcResponse.Success(id, new ToolResult(
                    Content: [new ToolContentBlock("text", "knowledge.search is unavailable: no active work item, no project scope.")],
                    IsError: true))
                : await McpKnowledgeSearch.SearchAsync(knowledgeSearcher, id, query, workerProject, arguments, cancellationToken)
            : await McpKnowledgeSearch.SearchAsync(
            knowledgeSearcher,
            id,
            query,
            McpArgumentReaders.ReadOptionalGuid(arguments, "projectId"),
            arguments,
            cancellationToken);
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

        var result = await knowledgeIngestor.IngestAsync(
            McpArgumentReaders.ReadOptionalGuid(arguments, "projectId"),
            title,
            source,
            sourceRef,
            mimeType,
            text,
            cancellationToken);

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

        var facts = await memoryStore.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Project,
                SubjectId: projectId.ToString(),
                Embedding: await McpMemoryEmbeddings.TryEmbedAsync(embedder, query),
                Limit: Math.Clamp(McpArgumentReaders.ReadOptionalInt(arguments, "topK") ?? RecallDefaultTopK, 1, RecallMaxTopK)),
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

        var written = await memoryStore.WriteAsync(
            new MemoryFactWrite(
                Scope: MemoryScope.Project,
                SubjectId: projectId.ToString(),
                Kind: McpArgumentReaders.ReadOptionalBool(arguments, "ephemeral") is true
                    ? MemoryFactKind.Ephemeral
                    : MemoryFactKind.Standing,
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

    /// <summary>
    /// learning.suggest — queue one rule candidate for human review, scoped
    /// to the worker's project (same enforcement as memory.note: the project
    /// comes from the lease, never from a client-supplied argument). A
    /// pending duplicate bumps its repeat counter; rate-limited per worker.
    /// </summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="caller">Resolved caller of the dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> LearningSuggestAsync(
        JsonElement? id,
        JsonElement arguments,
        McpCaller caller,
        CancellationToken cancellationToken)
    {
        if (caller.Worker is not { ProjectId: { } projectId } worker)
        {
            return JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", "learning.suggest is available only to a worker with an active work item.")],
                IsError: true));
        }

        var topic = McpArgumentReaders.ReadString(arguments, "topic");
        var observation = McpArgumentReaders.ReadString(arguments, "observation");
        var proposedRule = McpArgumentReaders.ReadString(arguments, "proposedRule");
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(observation) || string.IsNullOrWhiteSpace(proposedRule))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "learning.suggest requires non-empty arguments.topic, arguments.observation and arguments.proposedRule",
                Data: null);
        }

        if (topic.Length > SuggestTopicMaxLength
            || observation.Length > SuggestObservationMaxLength
            || proposedRule.Length > SuggestRuleMaxLength)
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                $"learning.suggest rejects arguments longer than topic {SuggestTopicMaxLength} / observation {SuggestObservationMaxLength} / proposedRule {SuggestRuleMaxLength} characters",
                Data: null);
        }

        if (!suggestRateLimiter.TryAcquire(worker.WorkerId))
        {
            return JsonRpcResponse.Success(id, new ToolResult(
                Content: [new ToolContentBlock("text", $"learning.suggest rate limit reached ({WorkerSuggestRateLimiter.Limit} per {WorkerSuggestRateLimiter.Window.TotalMinutes} minutes) — only non-obvious insights worth a human's review belong here.")],
                IsError: true));
        }

        var queued = await learningCandidates.SuggestAsync(
            new LearningSuggestion(
                ProjectId: projectId,
                Topic: topic,
                Observation: observation,
                ProposedRule: proposedRule,
                SourceRef: $"worker:{worker.WorkerId.Value}"),
            clock.GetUtcNow(),
            cancellationToken);

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock(
                "text",
                $"queued '{queued.Topic}' for human review ({queued.Status}){(queued.RepeatCount > 1 ? $" — {queued.RepeatCount} workers have now suggested this" : string.Empty)}")],
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

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(await runsList.ListAsync(query, cancellationToken), JsonSerializerOptions.Web))],
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

    /// <summary>observability.logs.search — LogsQL over VictoriaLogs.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> ObservabilityLogsSearchAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!McpObservabilityTools.TryParseLogSearch(id, arguments, out var query, out var error))
        {
            return error!;
        }

        IReadOnlyList<LogRow> rows;
        try
        {
            rows = await logsQueryClient.SearchAsync(query, cancellationToken);
        }
        catch (VictoriaUnavailableException exception)
        {
            return McpObservabilityTools.VictoriaUnavailable(id, exception);
        }

        return JsonRpcResponse.Success(id, McpObservabilityTools.ToolResultFromRows(rows));
    }

    /// <summary>observability.logs.context — fetch the logs of a single trace id.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> ObservabilityLogsContextAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var traceId = McpArgumentReaders.ReadString(arguments, "traceId");
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return McpObservabilityTools.InvalidParams(id, "observability.logs.context requires a non-empty arguments.traceId");
        }

        if (!McpObservabilityTools.TryReadWindow(id, arguments, out var from, out var to, out var error))
        {
            return error!;
        }

        IReadOnlyList<LogRow> rows;
        try
        {
            rows = await logsQueryClient.ContextAsync(
                traceId,
                from,
                to,
                McpObservabilityTools.ClampLimit(McpArgumentReaders.ReadOptionalInt(arguments, "limit")),
                cancellationToken);
        }
        catch (VictoriaUnavailableException exception)
        {
            return McpObservabilityTools.VictoriaUnavailable(id, exception);
        }

        return JsonRpcResponse.Success(id, McpObservabilityTools.ToolResultFromRows(rows));
    }

    /// <summary>observability.metrics.query — PromQL instant or range query.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> ObservabilityMetricsQueryAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var query = McpArgumentReaders.ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpObservabilityTools.InvalidParams(id, "observability.metrics.query requires a non-empty arguments.query");
        }

        DateTimeOffset? time;
        DateTimeOffset? start;
        DateTimeOffset? end;
        try
        {
            time = McpArgumentReaders.ReadOptionalIso8601(arguments, "time");
            start = McpArgumentReaders.ReadOptionalIso8601(arguments, "start");
            end = McpArgumentReaders.ReadOptionalIso8601(arguments, "end");
        }
        catch (ArgumentException exception)
        {
            return McpObservabilityTools.InvalidParams(id, $"observability.metrics.query {exception.Message}");
        }

        if ((time is null && (start is null || end is null))
            || (time is not null && (start is not null || end is not null)))
        {
            return McpObservabilityTools.InvalidParams(id, "observability.metrics.query requires either arguments.time (instant) or arguments.start+end (range), exclusively.");
        }

        if (start is not null && end is not null && end < start)
        {
            return McpObservabilityTools.InvalidParams(id, "observability.metrics.query rejects arguments.end < arguments.start");
        }

        var step = McpArgumentReaders.ReadOptionalInt(arguments, "step") is { } stepValue
            ? TimeSpan.FromSeconds(stepValue)
            : (TimeSpan?)null;

        var metricsQuery = time is not null
            ? new MetricsQuery(PromQl: query, Time: time)
            : new MetricsQuery(PromQl: query, Start: start, End: end, Step: step);

        IReadOnlyList<MetricSeries> series;
        try
        {
            series = await metricsQueryClient.QueryAsync(metricsQuery, cancellationToken);
        }
        catch (VictoriaUnavailableException exception)
        {
            return McpObservabilityTools.VictoriaUnavailable(id, exception);
        }

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(new
            {
                resultType = series.Count == 0 ? "empty" : (time is not null ? "vector" : "matrix"),
                seriesCount = series.Count,
                series = series.Select(static s => new
                {
                    labels = s.Labels,
                    sampleCount = s.Samples.Count,
                }),
            }, JsonSerializerOptions.Web))],
            IsError: false));
    }

    /// <summary>observability.metrics.series — list matching metric series for a label selector.</summary>
    /// <param name="id">JSON-RPC request id.</param>
    /// <param name="arguments">Parsed JSON arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonRpcResponse> ObservabilityMetricsSeriesAsync(
        JsonElement? id,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var match = McpArgumentReaders.ReadString(arguments, "match");
        if (string.IsNullOrWhiteSpace(match))
        {
            return McpObservabilityTools.InvalidParams(id, "observability.metrics.series requires a non-empty arguments.match");
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> labelKeys;
        try
        {
            labelKeys = await metricsQueryClient.SeriesAsync(match, cancellationToken);
        }
        catch (VictoriaUnavailableException exception)
        {
            return McpObservabilityTools.VictoriaUnavailable(id, exception);
        }

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(labelKeys, JsonSerializerOptions.Web))],
            IsError: false));
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
        var hits = await knowledgeSearcher.SearchAsync(
            query,
            projectId,
            McpArgumentReaders.ReadOptionalInt(arguments, "topK") ?? 5,
            McpArgumentReaders.ReadOptionalFloat(arguments, "minSimilarity") ?? 0.5f,
            cancellationToken);
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

/// <summary>
/// Observability-tool helpers: pure-function clamp / parse / envelope
/// routines the four <c>observability.*</c> tool handlers share. Lives
/// next to the handlers (file-scoped static class) per the canon — the
/// helpers are stateless, the dispatch lives in the handler methods.
/// </summary>
file static class McpObservabilityTools
{
    /// <summary>Upper bound on the time-range inputs the observability tools accept.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    /// <summary>Default page size for observability.* row-set tools when the caller omits <c>limit</c>.</summary>
    public const int DefaultLimit = 100;

    /// <summary>Upper bound for observability.* row-set windows (server caps at the same value).</summary>
    public const int MaxLimit = 1000;

    /// <summary>
    /// Parse the observability.logs.search arguments: the LogsQL body
    /// (required), the optional time window (ISO 8601), and the optional
    /// <c>limit</c> clamp. The window guard runs before any HTTP call so
    /// a misconfigured client doesn't get to chew through a full
    /// VictoriaLogs 30-day query only to be told the result is too big.
    /// </summary>
    public static bool TryParseLogSearch(
        JsonElement? id,
        JsonElement arguments,
        out LogsQuery query,
        out JsonRpcResponse? error)
    {
        var body = McpArgumentReaders.ReadString(arguments, "query");
        if (string.IsNullOrWhiteSpace(body))
        {
            query = default!;
            error = InvalidParams(id, "observability.logs.search requires a non-empty arguments.query");
            return false;
        }

        if (!TryReadWindow(id, arguments, out var from, out var to, out error))
        {
            query = default!;
            return false;
        }

        query = new LogsQuery(
            Query: body,
            From: from,
            To: to,
            Limit: ClampLimit(McpArgumentReaders.ReadOptionalInt(arguments, "limit")),
            TraceId: McpArgumentReaders.ReadOptionalString(arguments, "traceId"));
        error = null;
        return true;
    }

    /// <summary>
    /// Read the ISO 8601 <c>from</c> / <c>to</c> window the observability
    /// tools accept, validating the bounds in the clamp. A
    /// non-parseable timestamp surfaces as a typed <c>-32602</c> failure
    /// (the MCP error-mapping layer translates to
    /// <c>code = observability.invalid_params</c>). An inverted or
    /// over-long window surfaces the same way — no HTTP call is made
    /// before the clamp fires.
    /// </summary>
    public static bool TryReadWindow(
        JsonElement? id,
        JsonElement arguments,
        out DateTimeOffset? from,
        out DateTimeOffset? to,
        out JsonRpcResponse? error)
    {
        try
        {
            from = McpArgumentReaders.ReadOptionalIso8601(arguments, "from");
            to = McpArgumentReaders.ReadOptionalIso8601(arguments, "to");
        }
        catch (ArgumentException exception)
        {
            from = null;
            to = null;
            error = InvalidParams(id, $"observability.* tools {exception.Message}");
            return false;
        }

        if (from is null || to is null)
        {
            error = null;
            return true;
        }

        if (to < from)
        {
            error = InvalidParams(id, "observability.* tools reject arguments.to < arguments.from");
            return false;
        }

        if (to - from > MaxRange)
        {
            error = InvalidParams(id, $"observability.* tools reject time windows longer than {MaxRange.TotalDays:0} days");
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Clamp the row-limit to the documented [1, max] range, defaulting when absent.</summary>
    public static int ClampLimit(int? requested)
    {
        var value = requested ?? DefaultLimit;
        return Math.Clamp(value, 1, MaxLimit);
    }

    /// <summary>JSON-RPC -32602 (InvalidParams) envelope helper.</summary>
    public static JsonRpcResponse InvalidParams(JsonElement? id, string message)
    {
        return JsonRpcResponse.Failure(id, JsonRpcEnvelope.ErrorCodes.InvalidParams, message, Data: null);
    }

    /// <summary>
    /// Tool-level error envelope for <see cref="VictoriaUnavailableException"/>.
    /// The exception's stable <c>observability.victoria_unavailable</c> code
    /// is surfaced as the <c>message</c> string so clients branch on it
    /// (mirrors <c>McpToolPermissionMap.PermissionDeniedCode</c>).
    /// </summary>
    public static JsonRpcResponse VictoriaUnavailable(JsonElement? id, VictoriaUnavailableException exception)
    {
        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text",
                $"{VictoriaUnavailableException.VictoriaUnavailableCode}: {exception.Endpoint} ({exception.InnerException?.Message ?? "no response within the timeout"})")],
            IsError: true));
    }

    /// <summary>Render a list of typed log rows as the tool payload (camelCase JSON, web options).</summary>
    public static ToolResult ToolResultFromRows(IReadOnlyList<LogRow> rows)
    {
        return new(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(new
            {
                count = rows.Count,
                rows = rows.Select(static row => new
                {
                    timestamp = row.Timestamp,
                    level = row.Level,
                    messageTemplate = row.MessageTemplate,
                    scopeJson = row.ScopeJson,
                    traceId = row.TraceId,
                    spanId = row.SpanId,
                }),
            }, JsonSerializerOptions.Web))],
            IsError: false);
    }
}
