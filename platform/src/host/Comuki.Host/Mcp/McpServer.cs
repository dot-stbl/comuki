using System.Text.Json;
using Comuki.Host.Runs;
using Comuki.Modules.Knowledge.Application;
using Comuki.Shared.Filtering.Ports;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// MCP tool dispatcher — <c>POST /api/v1/mcp</c> speaks JSON-RPC 2.0
/// over HTTP, with a tools-shaped surface modelled on the MCP
/// <c>tools/call</c> convention (without dragging the full MCP SDK
/// in — the wire format is small and stable). Four tools are
/// registered; missing <c>tools/call</c> names surface as
/// <see cref="JsonRpcEnvelope.ErrorCodes.MethodNotFound"/>.
/// </summary>
public sealed class McpServer(
    IKnowledgeIngestor knowledgeIngestor,
    IKnowledgeSearcher knowledgeSearcher,
    RunsListHandler runsList,
    ILogger<McpServer> logger)
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Dispatches a single JSON-RPC 2.0 envelope — null id on a notification
    /// (the caller is fire-and-forget) returns null and the endpoint
    /// replies 204 No Content.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public async Task<JsonRpcResponse?> DispatchAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        return request is null || request.JsonRpc != JsonRpcEnvelope.Version
            ? JsonRpcResponse.Failure(
                request?.Id,
                JsonRpcEnvelope.ErrorCodes.InvalidRequest,
                "jsonrpc field must be \"2.0\"",
                Data: null)
            : request.Method switch
            {
                "tools/list" => ListToolsAsync(request.Id, cancellationToken),
                "tools/call" => await CallToolAsync(request.Id, request.Params, cancellationToken).ConfigureAwait(false),
                _ => JsonRpcResponse.Failure(
                    request.Id,
                    JsonRpcEnvelope.ErrorCodes.MethodNotFound,
                    $"unknown method '{request.Method}'",
                    Data: null),
            };
    }

    private static JsonRpcResponse ListToolsAsync(JsonElement? id, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var tools = new object[]
        {
            new
            {
                name = "knowledge.search",
                description = "Search the knowledge base by semantic similarity (pgvector cosine distance).",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new { type = "string", description = "Natural-language search query." },
                        ["projectId"] = new { type = "string", description = "Optional project scope; omit for global." },
                        ["topK"] = new { type = "integer", description = "Maximum number of hits (default 5)." },
                        ["minSimilarity"] = new { type = "number", description = "Cosine-similarity floor in [0.0, 1.0] (default 0.5)." },
                    },
                    required = new[] { "query" },
                },
            },
            new
            {
                name = "knowledge.ingest",
                description = "Ingest a source of knowledge content (chunked + embedded into pgvector).",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new { type = "string", description = "Human-readable title." },
                        ["source"] = new { type = "string", description = "Origin kind — git | upload | url." },
                        ["sourceRef"] = new { type = "string", description = "Origin pointer (URL, commit, blob id)." },
                        ["mimeType"] = new { type = "string", description = "Detected MIME type (text/markdown, …)." },
                        ["text"] = new { type = "string", description = "Raw text the worker chunks + embeds." },
                        ["projectId"] = new { type = "string", description = "Optional project scope." },
                    },
                    required = new[] { "title", "source", "sourceRef", "mimeType", "text" },
                },
            },
            new
            {
                name = "runs.list",
                description = "List runs — optional projectId + status filter, paged.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["projectId"] = new { type = "string", description = "Project scope (Guid string)." },
                        ["status"] = new { type = "string", description = "Status filter (queued | waiting | running | …)." },
                    },
                },
            },
            new
            {
                name = "runs.get",
                description = "Fetch one run by id.",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["runId"] = new { type = "string", description = "Run id (Guid)." },
                    },
                    required = new[] { "runId" },
                },
            },
        };

        return JsonRpcResponse.Success(id, new { tools });
    }

    private async Task<JsonRpcResponse> CallToolAsync(JsonElement? id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (parameters is null || parameters.Value.ValueKind != JsonValueKind.Object)
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "tools/call requires a params object",
                Data: null);
        }

        ToolCallParams? toolCall;
        try
        {
            toolCall = parameters.Value.Deserialize<ToolCallParams>(jsonOptions);
        }
        catch (JsonException exception)
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                $"tools/call params parse error: {exception.Message}",
                Data: null);
        }

        if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.Name))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "tools/call requires a non-empty params.name",
                Data: null);
        }

        try
        {
            return toolCall.Name switch
            {
                "knowledge.search" => await KnowledgeSearchAsync(id, toolCall.Arguments, cancellationToken).ConfigureAwait(false),
                "knowledge.ingest" => await KnowledgeIngestAsync(id, toolCall.Arguments, cancellationToken).ConfigureAwait(false),
                "runs.list" => await RunsListAsync(id, toolCall.Arguments, cancellationToken).ConfigureAwait(false),
                "runs.get" => await RunsGetAsync(id, toolCall.Arguments, cancellationToken).ConfigureAwait(false),
                _ => JsonRpcResponse.Failure(
                    id,
                    JsonRpcEnvelope.ErrorCodes.MethodNotFound,
                    $"unknown tool '{toolCall.Name}'",
                    Data: null),
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "mcp tool {Tool} failed", toolCall.Name);
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InternalError,
                $"tool '{toolCall.Name}' failed: {exception.Message}",
                Data: null);
        }
    }

    private async Task<JsonRpcResponse> KnowledgeSearchAsync(JsonElement? id, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var argumentsObject = arguments ?? default;
        var query = ReadString(argumentsObject, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "knowledge.search requires a non-empty arguments.query",
                Data: null);
        }

        var projectId = ReadOptionalGuid(argumentsObject, "projectId");
        var topK = ReadOptionalInt(argumentsObject, "topK") ?? 5;
        var minSimilarity = ReadOptionalFloat(argumentsObject, "minSimilarity") ?? 0.5f;

        var hits = await knowledgeSearcher.SearchAsync(query, projectId, topK, minSimilarity, cancellationToken).ConfigureAwait(false);
        var payload = hits.Select(static hit => new
        {
            chunkId = hit.ChunkId.ToString(),
            sourceDocumentId = hit.SourceDocumentId.ToString(),
            similarity = hit.Similarity,
            chunkText = hit.ChunkText,
        }).ToArray();

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(payload, jsonOptions))],
            IsError: false));
    }

    private async Task<JsonRpcResponse> KnowledgeIngestAsync(JsonElement? id, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var argumentsObject = arguments ?? default;
        var title = ReadString(argumentsObject, "title");
        var source = ReadString(argumentsObject, "source");
        var sourceRef = ReadString(argumentsObject, "sourceRef");
        var mimeType = ReadString(argumentsObject, "mimeType");
        var text = ReadString(argumentsObject, "text");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(mimeType) || string.IsNullOrWhiteSpace(text))
        {
            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "knowledge.ingest requires arguments.title, source, sourceRef, mimeType, text",
                Data: null);
        }

        var projectId = ReadOptionalGuid(argumentsObject, "projectId");
        var result = await knowledgeIngestor.IngestAsync(projectId, title, source, sourceRef, mimeType, text, cancellationToken).ConfigureAwait(false);

        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(new
            {
                sourceDocumentId = result.SourceDocumentId.ToString(),
                chunksWritten = result.ChunksWritten,
            }, jsonOptions))],
            IsError: false));
    }

    private async Task<JsonRpcResponse> RunsListAsync(JsonElement? id, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var argumentsObject = arguments ?? default;
        var projectId = ReadOptionalGuid(argumentsObject, "projectId");
        var status = ReadOptionalString(argumentsObject, "status");

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

        var page = await runsList.ListAsync(query, cancellationToken).ConfigureAwait(false);
        return JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", JsonSerializer.Serialize(page, jsonOptions))],
            IsError: false));
    }

    private static Task<JsonRpcResponse> RunsGetAsync(JsonElement? id, JsonElement? arguments, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var argumentsObject = arguments ?? default;
        var runIdText = ReadString(argumentsObject, "runId");
        if (string.IsNullOrWhiteSpace(runIdText) || !Guid.TryParse(runIdText, out var parsedRunId))
        {
            return Task.FromResult(JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidParams,
                "runs.get requires arguments.runId as a Guid string",
                Data: null));
        }

        // The existing runs surface exposes list + decision endpoints;
        // a single-run get-by-id helper lands in a later slice alongside
        // a dedicated GET /api/v1/runs/{id}. Until then surface the
        // gap as a tool error (vs. a JSON-RPC error) so the caller can
        // see the feature is not yet implemented.
        _ = parsedRunId;
        _ = new RunId(parsedRunId);
        return Task.FromResult(JsonRpcResponse.Success(id, new ToolResult(
            Content: [new ToolContentBlock("text", $"runs.get for {runIdText} is not yet implemented; use runs.list and filter by runId, or wait for a follow-up slice.")],
            IsError: true)));
    }

    private static string ReadString(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string? ReadOptionalString(JsonElement arguments, string propertyName)
    {
        var value = ReadString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Guid? ReadOptionalGuid(JsonElement arguments, string propertyName)
    {
        var raw = ReadOptionalString(arguments, propertyName);
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static int? ReadOptionalInt(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                {
                    return value;
                }

                if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static float? ReadOptionalFloat(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetSingle(out var value))
                {
                    return value;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && float.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }
}

/// <summary>JSON-RPC 2.0 envelope — either <see cref="JsonRpcSuccess"/> or <see cref="JsonRpcError"/>.</summary>
/// <remarks>
/// The two flavours live behind a discriminated union written as a
/// sealed record with a nullable body; the JSON serializer writes the
/// non-null branch and drops the null one (configured by the
/// dispatcher at the endpoint).
/// </remarks>
public abstract record JsonRpcResponse
{
    /// <summary>Wraps a <see cref="JsonRpcSuccess"/> for the dispatcher result.</summary>
    /// <param name="id"></param>
    /// <param name="result"></param>
    public static JsonRpcResponse Success(JsonElement? id, object result)
    {
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        return new JsonRpcResponseSuccess(id, json);
    }

    /// <summary>Wraps a <see cref="JsonRpcError"/> for the dispatcher result.</summary>
    /// <param name="id"></param>
    /// <param name="code"></param>
    /// <param name="message"></param>
    /// <param name="Data"></param>
    public static JsonRpcResponse Failure(JsonElement? id, int code, string message, object? Data)
    {
        return new JsonRpcResponseError(id, new JsonRpcErrorBody(code, message, Data));
    }

    private sealed record JsonRpcResponseSuccess(JsonElement? Id, JsonElement Result)
        : JsonRpcResponse;

    private sealed record JsonRpcResponseError(JsonElement? Id, JsonRpcErrorBody Error)
        : JsonRpcResponse;
}
