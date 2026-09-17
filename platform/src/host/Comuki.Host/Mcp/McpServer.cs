using System.Text.Json;
using Comuki.Modules.Identity.Application.Authorization;

namespace Comuki.Host.Mcp;

/// <summary>
/// MCP tool dispatcher — <c>POST /api/v1/mcp</c> speaks JSON-RPC 2.0
/// over HTTP, with a tools-shaped surface modelled on the MCP
/// <c>tools/call</c> convention (without dragging the full MCP SDK
/// in — the wire format is small and stable). Tool listing lives in
/// <see cref="McpToolCatalog"/>; per-tool handlers live in
/// <see cref="McpToolHandlers"/>; argument readers in
/// <see cref="McpArgumentReaders"/>. Every tool call is gated by the
/// resolved caller — <see cref="McpToolPermissionMap"/> for cookie /
/// api-key subjects, <see cref="McpWorkerToolGate"/> for worker-token
/// callers; the gate fires before the handler runs, with a deny default.
/// </summary>
public sealed class McpServer(
    McpToolHandlers toolHandlers,
    IPermissionEvaluator permissionEvaluator,
    ILogger<McpServer> logger)
{
    /// <summary>
    /// Dispatches a single JSON-RPC 2.0 envelope — null id on a notification
    /// (the caller is fire-and-forget) returns null and the endpoint
    /// replies 204 No Content.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="caller">
    /// Resolved caller of the dispatch: a cookie / api-key subject, a
    /// worker-token caller (project scope pre-resolved from its lease), or
    /// <see cref="McpCaller.Anonymous"/> — anonymous callers are denied
    /// every tool; the endpoint never answers unauthenticated traffic.
    /// </param>
    /// <param name="cancellationToken"></param>
    public async Task<JsonRpcResponse?> DispatchAsync(
        JsonRpcRequest request,
        McpCaller caller,
        CancellationToken cancellationToken = default)
    {
        return request is null || request.JsonRpc != JsonRpcEnvelope.Version
            ? JsonRpcResponse.Failure(
                request?.Id,
                JsonRpcEnvelope.ErrorCodes.InvalidRequest,
                "jsonrpc field must be \"2.0\"",
                Data: null)
            : request.Method switch
            {
                "tools/list" => McpToolCatalog.List(request.Id),
                "tools/call" => await McpToolCallDispatch.CallAsync(
                    toolHandlers, permissionEvaluator, logger, request.Id, request.Params, caller, cancellationToken),
                _ => JsonRpcResponse.Failure(
                    request.Id,
                    JsonRpcEnvelope.ErrorCodes.MethodNotFound,
                    $"unknown method '{request.Method}'",
                    Data: null),
            };
    }
}

/// <summary>
/// The <c>tools/call</c> body of the dispatcher: parse the tool-call
/// params, run the caller gate, then hand the call to its handler —
/// envelope-level errors (bad params, deny, unknown tool, handler fault)
/// come back as JSON-RPC error responses.
/// </summary>
file static class McpToolCallDispatch
{
    public static async Task<JsonRpcResponse> CallAsync(
        McpToolHandlers toolHandlers,
        IPermissionEvaluator permissionEvaluator,
        ILogger logger,
        JsonElement? id,
        JsonElement? parameters,
        McpCaller caller,
        CancellationToken cancellationToken)
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
            toolCall = parameters.Value.Deserialize<ToolCallParams>(JsonSerializerOptions.Web);
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

        // Caller gate (security audit A01-1): every tool call is denied
        // unless the resolved caller passes its gate — a cookie / api-key
        // subject needs the per-tool permission (McpToolPermissionMap), a
        // worker-token caller is confined to the worker tool set with a
        // resolvable project (McpWorkerToolGate), and an anonymous caller
        // is denied outright. Tools outside a gate's set are denied by
        // default. JSON-RPC -32600 (InvalidRequest) is the wire format for
        // the deny — the error message carries the stable
        // "permission.denied" code so clients branch on it.
        if (!await McpCallerGate.IsAllowedAsync(toolCall.Name, caller, permissionEvaluator, cancellationToken))
        {
            logger.LogWarning(
                "MCP tool {Tool} denied for caller {Caller}",
                toolCall.Name,
                caller.Subject is { } subject
                    ? subject.ToString()
                    : caller.Worker is { } worker ? $"worker {worker.WorkerId.Value}" : "<anonymous>");

            return JsonRpcResponse.Failure(
                id,
                JsonRpcEnvelope.ErrorCodes.InvalidRequest,
                McpToolPermissionMap.PermissionDeniedCode,
                Data: null);
        }

        var arguments = toolCall.Arguments ?? default;
        try
        {
            return toolCall.Name switch
            {
                "knowledge.search" => await toolHandlers.KnowledgeSearchAsync(id, arguments, caller, cancellationToken),
                "knowledge.ingest" => await toolHandlers.KnowledgeIngestAsync(id, arguments, cancellationToken),
                "memory.recall" => await toolHandlers.MemoryRecallAsync(id, arguments, caller, cancellationToken),
                "memory.note" => await toolHandlers.MemoryNoteAsync(id, arguments, caller, cancellationToken),
                "learning.suggest" => await toolHandlers.LearningSuggestAsync(id, arguments, caller, cancellationToken),
                "runs.list" => await toolHandlers.RunsListAsync(id, arguments, cancellationToken),
                "runs.get" => await McpToolHandlers.RunsGetAsync(id, arguments),
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
}

/// <summary>
/// The caller-type dispatch of the gate: subject → permission map,
/// worker → worker tool gate, neither → deny.
/// </summary>
file static class McpCallerGate
{
    public static async Task<bool> IsAllowedAsync(
        string toolName,
        McpCaller caller,
        IPermissionEvaluator permissionEvaluator,
        CancellationToken cancellationToken)
    {
        return caller.Subject is { } subject
            ? await McpToolPermissionMap.IsAllowedAsync(toolName, subject, permissionEvaluator, cancellationToken)
            : caller.Worker is { } worker && McpWorkerToolGate.IsAllowed(toolName, worker);
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
        var json = JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web);

        return new JsonRpcResponseSuccess(JsonRpcEnvelope.Version, id, json);
    }

    /// <summary>Wraps a <see cref="JsonRpcError"/> for the dispatcher result.</summary>
    /// <param name="id"></param>
    /// <param name="code"></param>
    /// <param name="message"></param>
    /// <param name="Data"></param>
    public static JsonRpcResponse Failure(JsonElement? id, int code, string message, object? Data)
    {
        return new JsonRpcResponseError(JsonRpcEnvelope.Version, id, new JsonRpcErrorBody(code, message, Data));
    }

    private sealed record JsonRpcResponseSuccess(
        [property: System.Text.Json.Serialization.JsonPropertyName("jsonrpc")] string JsonRpc,
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] JsonElement? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("result")] JsonElement Result)
        : JsonRpcResponse;

    private sealed record JsonRpcResponseError(
        [property: System.Text.Json.Serialization.JsonPropertyName("jsonrpc")] string JsonRpc,
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] JsonElement? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] JsonRpcErrorBody Error)
        : JsonRpcResponse;
}
