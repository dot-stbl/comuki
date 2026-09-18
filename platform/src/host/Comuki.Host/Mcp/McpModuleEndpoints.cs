using System.Text.Json;
using Comuki.Host.Auth.Security;
using Comuki.Host.Workers;

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
        // Anonymous — the resolved caller drives the gates. The host's
        // cookie / api-key auth resolves dashboard subjects; a swarm
        // worker presents its worker token as a Bearer credential and is
        // resolved below into a project-scoped worker caller. The gate
        // itself lives in the dispatcher (security audit A01-1): an
        // anonymous caller (neither subject nor worker) is denied there.
        app.MapPost(ApiRoutes.Mcp, DispatchAsync).WithTags("Mcp");
        return app;
    }

    private static async Task<IResult> DispatchAsync(
        HttpContext context,
        McpServer server,
        CancellationToken cancellationToken)
    {
        var caller = await McpCallerResolution.ResolveAsync(context, cancellationToken);

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

        var response = await server.DispatchAsync(envelope, caller, cancellationToken);

        // JSON-RPC notifications carry no id and the spec says the
        // endpoint must not respond. 204 No Content is the closest
        // .NET analogue that still carries no body.
        return response is null ? Results.NoContent() : Results.Json(response, JsonSerializerOptions.Web, statusCode: StatusCodes.Status200OK);
    }
}

/// <summary>
/// Caller resolution for one MCP dispatch, in precedence order: the
/// cookie / api-key principal wins (the dashboard surface), then the
/// worker Bearer token (the swarm surface — project resolved server-side
/// from the lease the token maps to), else anonymous.
/// </summary>
file static class McpCallerResolution
{
    public static async Task<McpCaller> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (HostSubjects.Resolve(context.User) is { } subject)
        {
            return new McpCaller(Subject: subject);
        }

        // The worker surface is opt-in per composition (AddWorkerRuntime);
        // hosts without it never see a worker Bearer header, so the
        // authenticator resolves lazily — only once a header says the
        // caller claims to be a worker. Compositions that do carry worker
        // traffic register the authenticator and the header path behaves
        // exactly as before.
        var token = WorkerTokenHeaders.TryGetFromHttp(context.Request.Headers);
        if (token is null)
        {
            return McpCaller.Anonymous;
        }

        var authenticator = context.RequestServices.GetRequiredService<WorkerTokenAuthenticator>();
        if (authenticator.Authenticate(token) is not { } workerId)
        {
            return McpCaller.Anonymous;
        }

        var projectResolver = context.RequestServices.GetRequiredService<IWorkerProjectResolver>();
        var projectId = await projectResolver.ResolveProjectAsync(workerId, cancellationToken);
        return new McpCaller(Worker: new McpWorkerCaller(workerId, projectId));
    }
}
