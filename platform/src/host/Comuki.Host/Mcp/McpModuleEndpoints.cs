using System.Text.Json;

namespace Comuki.Host.Mcp;

/// <summary>
/// JSON-RPC 2.0 envelope surface for <c>POST /api/v1/mcp</c>. The endpoint
/// speaks the canonical jsonrpc 2.0 framing (request / response / error)
/// and a tools-shaped call surface modelled on the MCP convention; the
/// full MCP SDK is intentionally NOT pulled in — the wire format is
/// small and stable, and skipping the SDK keeps the host dependency
/// surface tight.
/// </summary>
public static class McpModuleEndpoints
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Maps the MCP JSON-RPC 2.0 endpoint.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous — the global auth + permission filter (when wired)
        // handles identity; MCP shares the host's cookie / api-key auth.
        app.MapPost(ApiRoutes.Mcp, DispatchAsync).WithTags("Mcp");
        return app;
    }

    private static async Task<IResult> DispatchAsync(
        HttpRequest request,
        McpServer server,
        CancellationToken cancellationToken)
    {
        JsonRpcRequest? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(request.Body, jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return Results.Json(
                JsonRpcResponse.Failure(
                    id: null,
                    code: JsonRpcEnvelope.ErrorCodes.ParseError,
                    message: $"JSON parse error: {exception.Message}",
                    Data: null),
                jsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (envelope is null)
        {
            return Results.Json(
                JsonRpcResponse.Failure(
                    id: null,
                    code: JsonRpcEnvelope.ErrorCodes.InvalidRequest,
                    message: "empty request body",
                    Data: null),
                jsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await server.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);

        // JSON-RPC notifications carry no id and the spec says the
        // endpoint must not respond. 204 No Content is the closest
        // .NET analogue that still carries no body.
        return response is null ? Results.NoContent() : Results.Json(response, jsonOptions, statusCode: StatusCodes.Status200OK);
    }
}
