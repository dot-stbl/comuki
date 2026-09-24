using Comuki.TestFakeModel.Cassettes.Matching;
using Comuki.TestFakeModel.Cassettes.Replay;
using Comuki.TestFakeModel.Cassettes.Sse;

namespace Comuki.TestFakeModel.Cassettes.Endpoints;

/// <summary>
/// Serves the loaded cassette for <em>any</em> POST path — one catch-all
/// route rather than mirroring <c>Anthropic</c>/<c>OpenAi</c>'s two named
/// routes, since the cassette itself records which path each exchange
/// belongs to and <see cref="CassetteRequestParser"/> dispatches on the
/// observed path, not the route template.
/// </summary>
public static class CassetteReplayEndpoint
{
    /// <summary>Registers the catch-all replay route on <paramref name="endpoints"/>.</summary>
    public static IEndpointRouteBuilder MapCassetteReplay(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CassetteEndpointIO.CatchAllRoutePath, HandleAsync);
        return endpoints;
    }

    // Minimal API endpoint handler — private static, referenced as a method group from
    // MapPost above. Exempt from class-layout-and-tooling.md §1a's private-method ban
    // (exemption #3): this is the one place the rule allows it. `catchAll` binds the
    // route template's wildcard capture (ASP0018 flags a declared-but-unbound route
    // parameter); the body reads the path from HttpContext.Request.Path instead, so
    // it's otherwise unused — IDE0060 is off repo-wide for exactly this shape (see
    // .editorconfig), no `_ = catchAll;` discard needed.
    private static async Task HandleAsync(HttpContext context, string catchAll, CassettePlaybackState state, CancellationToken cancellationToken)
    {
        using var bodyReader = new StreamReader(context.Request.Body);
        var path = context.Request.Path.Value ?? string.Empty;

        if (!CassetteEndpointIO.TryParseBody(await bodyReader.ReadToEndAsync(cancellationToken), out var document, out var parseError))
        {
            await CassetteEndpointIO.WriteInvalidRequestAsync(context, path, $"malformed JSON body: {parseError}", cancellationToken);
            return;
        }

        using var disposableDocument = document;

        CassetteExchange exchange;
        try
        {
            exchange = state.Resolve(state.NextRequestIndex(), context.Request.Method, path, CassetteRequestParser.Parse(path, document.RootElement));
        }
        catch (CassetteMismatchException ex)
        {
            await CassetteEndpointIO.WriteFailureAsync(context, path, ex.Message, cancellationToken);
            return;
        }

        context.Response.StatusCode = exchange.Response.Status;
        if (exchange.Response.Streamed)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await CassetteSseResponseWriter.WriteAsync(context.Response, exchange.Response.Events ?? [], cancellationToken);
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(exchange.Response.Body?.GetRawText() ?? "null", cancellationToken);
    }
}
