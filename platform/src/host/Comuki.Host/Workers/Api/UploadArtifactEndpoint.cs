using Comuki.Modules.Artifacts.Application.VisualArtifacts;
using Comuki.Shared.Kernel.Scoping;

namespace Comuki.Host.Workers.Api;

/// <summary>
/// Worker-side publish endpoint (issue #51 slice 1):
/// <c>POST /workers/{workItemId}/artifacts</c> with a multipart
/// form-data body containing one <c>file</c> part. The host stamps
/// run + project from the leased work item, validates size against
/// the per-mime cap, and persists the bytes to MinIO. The
/// endpoint mirrors the worker claim/heartbeat/complete auth model
/// (bearer token, 401 unauthenticated, 409 not owner, 413 too big,
/// 415 unsupported mime).
/// </summary>
public static class UploadArtifactEndpoint
{
    /// <summary>Route constant — paired with <see cref="ApiRoutes"/>.</summary>
    public const string RouteTemplate = ApiRoutes.WorkerUploadArtifact;

    /// <summary>Maps the endpoint onto <paramref name="app"/>.</summary>
    /// <param name="app"></param>
    public static void MapUploadArtifactEndpoint(WebApplication app)
    {
        app.MapPost(RouteTemplate, UploadArtifactAsync);
    }

    private static async Task<IResult> UploadArtifactAsync(
        Guid workItemId,
        HttpContext httpContext,
        WorkerTokenAuthenticator authenticator,
        ISubjectScopeAccessor scopeAccessor,
        VisualArtifactService service,
        CancellationToken cancellationToken)
    {
        if (WorkerEndpointHelpers.AuthenticateWorker(authenticator, httpContext) is not { } workerId)
        {
            return WorkerResults.Unauthenticated();
        }

        // The worker runtime is a platform-system consumer for the
        // scope axis — the lease check is the per-project authority;
        // the subject-scope query filter would otherwise fail open
        // for a request path that has not crossed SubjectScopeMiddleware.
        using var systemScope = scopeAccessor.AsSystem("worker-artifact-upload");

        var parsed = await UploadArtifactParsingHelpers
            .ReadMultipartFileAsync(httpContext.Request, cancellationToken)
            ;
        if (parsed.Failure is not null)
        {
            return parsed.Failure;
        }

        var outcome = await service.PublishFromWorkerAsync(
            workItemId,
            workerId,
            parsed.Filename,
            parsed.ContentType,
            parsed.SizeBytes,
            parsed.File,
            title: null,
            cancellationToken);

        return UploadArtifactResultMapper.FromOutcome(outcome);
    }
}

/// <summary>Maps the typed <see cref="VisualArtifactPublishOutcome"/> onto an HTTP result.</summary>
internal static class UploadArtifactResultMapper
{
    /// <summary>Translates each outcome variant onto its HTTP status.</summary>
    /// <param name="outcome"></param>
    public static IResult FromOutcome(VisualArtifactPublishOutcome outcome)
    {
        return outcome switch
        {
            VisualArtifactPublishOutcome.PublishedRecord =>
                Results.NoContent(),
            VisualArtifactPublishOutcome.NotOwned =>
                WorkerResults.NotOwner(),
            VisualArtifactPublishOutcome.SizeExceeded exceeded =>
                TypedResults.Problem(
                    title: "Payload too large",
                    detail: $"payload {exceeded.SizeBytes} bytes exceeds the cap of {exceeded.MaxBytes} bytes for {exceeded.ContentType}",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "artifact.size_exceeded",
                        ["contentType"] = exceeded.ContentType,
                        ["sizeBytes"] = exceeded.SizeBytes,
                        ["maxBytes"] = exceeded.MaxBytes,
                    }),
            VisualArtifactPublishOutcome.UnsupportedMime unsupported =>
                TypedResults.Problem(
                    title: "Unsupported content type",
                    detail: $"content type {unsupported.ContentType} is not on the allow-list (png, html, svg)",
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "artifact.unsupported_mime",
                        ["contentType"] = unsupported.ContentType,
                    }),
            _ => TypedResults.Problem(
                title: "Publish failed",
                detail: "unknown outcome",
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}

/// <summary>
/// File-static helpers for the worker upload endpoint. Extracted so
/// <see cref="UploadArtifactEndpoint"/> holds only orchestration —
/// per <c>class-layout-and-tooling.md §1a</c>, helper logic lives in
/// <c>file static class</c>es next to the consumer.
/// </summary>
file static class UploadArtifactParsingHelpers
{
    /// <summary>
    /// Reads the multipart body and returns the parsed upload, or a
    /// <see cref="Failure"/> with the appropriate 400 ProblemDetails
    /// response. The stream is an <see cref="IFormFile.OpenReadStream"/>
    /// over ASP.NET's buffered body — the caller passes it through to
    /// the store, which streams it to MinIO.
    /// </summary>
    /// <param name="httpRequest"></param>
    /// <param name="cancellationToken"></param>
    public static async Task<ParsedMultipartFile> ReadMultipartFileAsync(
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValueHelpers.IsMultipart(httpRequest.ContentType))
        {
            return ParsedMultipartFile.WithFailure(TypedResults.Problem(
                title: "Multipart expected",
                detail: "the worker upload endpoint requires multipart/form-data",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "artifact.multipart_required" }));
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null)
        {
            return ParsedMultipartFile.WithFailure(TypedResults.Problem(
                title: "Missing file",
                detail: "the multipart body must contain a 'file' part",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "artifact.file_required" }));
        }

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var filename = string.IsNullOrWhiteSpace(file.FileName) ? "artifact" : file.FileName;
        var stream = file.OpenReadStream();
        var sizeBytes = file.Length;

        return ParsedMultipartFile.WithSuccess(stream, contentType, filename, sizeBytes);
    }
}

/// <summary>
/// Result of <see cref="UploadArtifactParsingHelpers.ReadMultipartFileAsync"/> —
/// either a <see cref="Failure"/> ProblemDetails, or a success carrying
/// the file stream + wire metadata.
/// </summary>
file sealed record ParsedMultipartFile(
    IResult? Failure,
    Stream File,
    string ContentType,
    string Filename,
    long SizeBytes)
{
    /// <summary>Wraps a 400 (or other early-return) response.</summary>
    /// <param name="failure">The ProblemDetails result to return.</param>
    public static ParsedMultipartFile WithFailure(IResult failure)
    {
        return new ParsedMultipartFile(failure, Stream.Null, string.Empty, string.Empty, 0);
    }

    /// <summary>Wraps a successful multipart parse.</summary>
    /// <param name="file"></param>
    /// <param name="contentType"></param>
    /// <param name="filename"></param>
    /// <param name="sizeBytes"></param>
    public static ParsedMultipartFile WithSuccess(
        Stream file,
        string contentType,
        string filename,
        long sizeBytes)
    {
        return new ParsedMultipartFile(null, file, contentType, filename, sizeBytes);
    }
}

/// <summary>Header / media-type helpers used by the multipart parser.</summary>
file static class MediaTypeHeaderValueHelpers
{
    /// <summary>True when <paramref name="contentType"/> starts with <c>multipart/</c>.</summary>
    /// <param name="contentType"></param>
    public static bool IsMultipart(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase);
    }
}
