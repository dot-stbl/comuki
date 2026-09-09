using System.Text.Json;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Host.Mcp;

/// <summary>
/// MCP tool dispatcher — <c>POST /api/v1/mcp</c> speaks JSON-RPC 2.0
/// over HTTP, with a tools-shaped surface modelled on the MCP
/// <c>tools/call</c> convention (without dragging the full MCP SDK
/// in — the wire format is small and stable). Tool listing lives in
/// <see cref="McpToolCatalog"/>; per-tool handlers live in
/// <see cref="McpToolHandlers"/>; argument readers in
/// <see cref="McpArgumentReaders"/>. Every tool is gated by the per-tool
/// permission map (<see cref="McpToolPermissionMap"/>); the gate fires
/// before the handler runs, with a deny default.
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
    /// <param name="subject">
    /// Resolved caller subject from the cookie / api-key principal. Null
    /// (anonymous) callers are denied every tool — the endpoint accepts
    /// cookie / api-key auth but never answers unauthenticated traffic.
    /// </param>
    /// <param name="cancellationToken"></param>
    public async Task<JsonRpcResponse?> DispatchAsync(
        JsonRpcRequest request,
        RoleSubject? subject,
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
                "tools/call" => await CallToolAsync(request.Id, request.Params, subject, cancellationToken),
                _ => JsonRpcResponse.Failure(
                    request.Id,
                    JsonRpcEnvelope.ErrorCodes.MethodNotFound,
                    $"unknown method '{request.Method}'",
                    Data: null),
            };
    }

    private async Task<JsonRpcResponse> CallToolAsync(
        JsonElement? id,
        JsonElement? parameters,
        RoleSubject? subject,
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

        // Per-tool permission gate (security audit A01-1): every tool call
        // is denied unless the resolved subject carries the required
        // permission. Anonymous callers (subject == null) are denied
        // outright; tools not in the map are denied by default. JSON-RPC
        // -32600 (InvalidRequest) is the wire format for the deny — the
        // error message carries the stable "permission.denied" code so
        // clients branch on it.
        if (!await McpToolPermissionMap.IsAllowedAsync(toolCall.Name, subject, permissionEvaluator, cancellationToken))
        {
            logger.LogWarning(
                "MCP tool {Tool} denied for subject {Subject}",
                toolCall.Name,
                subject is null ? "<anonymous>" : subject.ToString());

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
                "knowledge.search" => await toolHandlers.KnowledgeSearchAsync(id, arguments, cancellationToken),
                "knowledge.ingest" => await toolHandlers.KnowledgeIngestAsync(id, arguments, cancellationToken),
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
