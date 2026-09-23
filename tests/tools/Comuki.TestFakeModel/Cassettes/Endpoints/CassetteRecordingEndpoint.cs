using Comuki.TestFakeModel.Cassettes.Matching;
using Comuki.TestFakeModel.Cassettes.Recording;

namespace Comuki.TestFakeModel.Cassettes.Endpoints;

/// <summary>
/// Forwards <em>any</em> POST path to the configured upstream and records
/// a redacted cassette exchange for it — same catch-all shape as
/// <see cref="CassetteReplayEndpoint"/> for the same reason.
/// </summary>
public static class CassetteRecordingEndpoint
{
    /// <summary>Registers the catch-all recording route on <paramref name="endpoints"/>.</summary>
    public static IEndpointRouteBuilder MapCassetteRecording(this IEndpointRouteBuilder endpoints)
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
    private static async Task HandleAsync(HttpContext context, string catchAll, CassetteRecordingState state, CancellationToken cancellationToken)
    {
        using var bodyReader = new StreamReader(context.Request.Body);
        var rawBody = await bodyReader.ReadToEndAsync(cancellationToken);
        var path = context.Request.Path.Value ?? string.Empty;

        if (!CassetteEndpointIO.TryParseBody(rawBody, out var document, out var parseError))
        {
            await CassetteEndpointIO.WriteInvalidRequestAsync(context, path, $"malformed JSON body: {parseError}", cancellationToken);
            return;
        }

        using var disposableDocument = document;

        // RecordAsync both writes the real (unredacted) upstream response onto
        // `context` and persists a redacted copy to the cassette; a refusal
        // here (Redaction.CassetteRedactionRefusedException) surfaces as an
        // unhandled exception — the live call the caller made already
        // succeeded, but the recording session itself must fail loudly
        // (design.md: refuse to write, not "probably fine"), not swallow the
        // refusal and leave a caller believing the cassette is complete.
        await state.RecordAsync(context, context.Request.Method, path, rawBody, CassetteRequestParser.Parse(path, document.RootElement), cancellationToken);
    }
}
