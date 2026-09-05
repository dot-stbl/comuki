using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Knowledge.Application;

namespace Comuki.Host.Knowledge;

/// <summary>
/// REST surface for the knowledge layer (S10 #9):
/// <c>POST /api/v1/knowledge/ingest</c> — permission
/// <c>knowledge:write</c>. The endpoint delegates straight to
/// <see cref="IKnowledgeIngestor"/>; the search path is exposed
/// through the MCP server (<c>knowledge.search</c>) plus a follow-up
/// <c>GET /api/v1/knowledge/search</c> in a later slice.
/// </summary>
public static class KnowledgeModuleEndpoints
{
    /// <summary>Maps the knowledge endpoints.</summary>
    /// <param name="app"></param>
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.KnowledgeIngest, IngestAsync).WithTags("Knowledge");
        return app;
    }

    [RequiresPermission("knowledge:write")]
    private static async Task<IResult> IngestAsync(
        KnowledgeIngestRequest request,
        IKnowledgeIngestor ingestor,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.Problem(
                title: "Knowledge ingest body required",
                detail: "POST /api/v1/knowledge/ingest requires a JSON body with title / source / sourceRef / mimeType / text",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await ingestor.IngestAsync(
            request.ProjectId,
            request.Title,
            request.Source,
            request.SourceRef,
            request.MimeType,
            request.Text,
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new KnowledgeIngestResponse(
            SourceDocumentId: result.SourceDocumentId.ToString(),
            ChunksWritten: result.ChunksWritten));
    }
}
