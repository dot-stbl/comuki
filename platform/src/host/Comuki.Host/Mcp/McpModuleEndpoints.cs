using System.Text.Json;
using Comuki.Host.Auth.Security;

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
    /// <summary>Maps the MCP JSON-RPC 2.0 endpoint.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous — the global auth + permission filter (when wired)
        // handles identity; MCP shares the host's cookie / api-key auth.
        // The per-tool permission gate lives in the dispatcher
        // (security audit A01-1): every tool call is denied unless the
        // resolved subject carries the required permission key. An
        // anonymous caller (no subject) is denied at the dispatcher.
        app.MapPost(ApiRoutes.Mcp, DispatchAsync).WithTags("Mcp");
        return app;
    }

    private static async Task<IResult> DispatchAsync(
        HttpContext context,
        McpServer server,
        CancellationToken cancellationToken)
    {
        var subject = HostSubjects.Resolve(context.User);

        JsonRpcRequest? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(context.Request.Body, JsonSerializerOptions.Web, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Results.Json(
                JsonRpcResponse.Failure(
                    id: null,
                    code: JsonRpcEnvelope.ErrorCodes.ParseError,
                    message: $"JSON parse error: {exception.Message}",
                    Data: null),
                JsonSerializerOptions.Web,
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
                JsonSerializerOptions.Web,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await server.DispatchAsync(envelope, subject, cancellationToken);

        // JSON-RPC notifications carry no id and the spec says the
        // endpoint must not respond. 204 No Content is the closest
        // .NET analogue that still carries no body.
        return response is null ? Results.NoContent() : Results.Json(response, JsonSerializerOptions.Web, statusCode: StatusCodes.Status200OK);
    }
}
