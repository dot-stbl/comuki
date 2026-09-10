using Comuki.Host.Artifacts.Models;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Artifacts;

/// <summary>
/// Visual-artifact content surface (issue #51 slice 1). The list
/// endpoint returns one page of metadata per project (run-filtered
/// when supplied); the content endpoint streams the bytes for one
/// artifact. Both require <c>run:read</c> — same surface the run
/// artifacts controller exposes. Out-of-scope rows surface as 404
/// (the object-axis filter handles that, never a deny).
/// </summary>
/// <param name="store">EF + MinIO-backed store.</param>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/artifacts")]
[RequiresPermission("run:read")]
public sealed class VisualArtifactsController(IVisualArtifactStore store) : ControllerBase
{
    /// <summary>
    /// One page of visual-artifact metadata for one project, oldest
    /// first. Empty when the project has no visual artifacts yet.
    /// </summary>
    /// <param name="projectId">Owning project (path).</param>
    /// <param name="runId">Optional run filter (query).</param>
    /// <param name="workItemId">Optional work-item filter (query).</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [EndpointName("artifacts-visual-list")]
    [ProducesResponseType<VisualArtifactPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisualArtifactPage>> ListAsync(
        Guid projectId,
        [FromQuery] Guid? runId,
        [FromQuery] Guid? workItemId,
        CancellationToken cancellationToken = default)
    {
        var items = await VisualArtifactsControllerQueries
            .ListAsync(store, new ProjectId(projectId), runId, workItemId, cancellationToken)
            ;
        return Ok(new VisualArtifactPage(items, projectId));
    }

    /// <summary>
    /// Streams the bytes of one visual artifact. <c>image/png</c>
    /// serves with a short cache + nosniff; <c>text/html</c> and
    /// <c>image/svg+xml</c> add a strict CSP that blocks
    /// same-origin / inline-eval / remote-frame — the FE embeds them
    /// in a sandboxed iframe, never the dashboard's frame.
    /// </summary>
    /// <param name="projectId">Owning project (path, scope filter).</param>
    /// <param name="artifactId">Artifact id (path).</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{artifactId:guid}/content")]
    [EndpointName("artifacts-visual-content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContentAsync(
        Guid projectId,
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        var content = await store.DownloadVisualInProjectAsync(
            new VisualArtifactId(artifactId),
            new ProjectId(projectId),
            cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        VisualArtifactsResponseHelpers.ApplyContentHeaders(Response, content.ContentType);
        return File(content.Body, content.ContentType, enableRangeProcessing: false);
    }
}

/// <summary>
/// File-static helpers for <see cref="VisualArtifactsController"/>.
/// Extracted so the controller class holds only the orchestration —
/// per <c>class-layout-and-tooling.md §1a</c>, helper logic lives in
/// <c>file static class</c>es next to the consumer.
/// </summary>
file static class VisualArtifactsResponseHelpers
{
    /// <summary>
    /// Sets the strict CSP for HTML / SVG responses and <c>nosniff</c>
    /// for everything else. The dashboard wraps the body in an
    /// <c>&lt;iframe sandbox="allow-scripts"&gt;</c> — same-origin is
    /// intentionally excluded so the embedded page cannot read the
    /// dashboard's cookies.
    /// </summary>
    /// <param name="response">The MVC response to mutate.</param>
    /// <param name="contentType">MIME type from the metadata row.</param>
    public static void ApplyContentHeaders(HttpResponse response, string contentType)
    {
        response.Headers.XContentTypeOptions = "nosniff";

        if (VisualArtifactLimits.IsHtml(contentType) || VisualArtifactLimits.IsSvg(contentType))
        {
            // Issue #51 / acceptance criterion 8: HTML in the pane
            // cannot read dashboard cookies / call same-origin APIs.
            // The CSP permits only the CDN sources the design system
            // documents use (Tailwind + Google Fonts).
            response.Headers.ContentSecurityPolicy =
                "default-src 'none'; img-src https: data:; style-src 'unsafe-inline' https://cdn.tailwindcss.com https://fonts.googleapis.com; script-src https://cdn.tailwindcss.com; font-src https://fonts.gstatic.com";
            response.Headers.CacheControl = "private, max-age=60";
        }
        else if (VisualArtifactLimits.IsImage(contentType))
        {
            response.Headers.CacheControl = "private, max-age=300";
        }
    }
}

/// <summary>
/// File-static helper for the visual-artifacts list endpoint. Holds
/// the EF-side projection so <see cref="VisualArtifactsController"/>
/// stays orchestration-only (per <c>class-layout-and-tooling.md §1a</c>).
/// </summary>
file static class VisualArtifactsControllerQueries
{
    /// <summary>
    /// Lists visual artifacts for one project, optionally filtered by
    /// run or work item. The store's scope query filter narrows the
    /// result set to rows in the subject's project scope.
    /// </summary>
    /// <param name="store">Visual-artifact store facade.</param>
    /// <param name="projectId">Owning project.</param>
    /// <param name="runId">Optional run filter.</param>
    /// <param name="workItemId">Optional work-item filter.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<IReadOnlyList<VisualArtifactListItem>> ListAsync(
        IVisualArtifactStore store,
        ProjectId projectId,
        Guid? runId,
        Guid? workItemId,
        CancellationToken cancellationToken)
    {
        var artifacts = await VisualArtifactsControllerQueriesHelpers
            .FetchAsync(store, projectId, runId, workItemId, cancellationToken)
            ;
        return VisualArtifactsControllerQueriesHelpers.Project(artifacts);
    }
}

/// <summary>EF-side projection + result mapping for the visual-artifacts list endpoint.</summary>
file static class VisualArtifactsControllerQueriesHelpers
{
    /// <summary>Fetches the filtered set of visual artifacts for one project.</summary>
    /// <param name="store">Visual-artifact store facade (unused in slice 1 — wired in slice 3).</param>
    /// <param name="projectId">Owning project (unused in slice 1).</param>
    /// <param name="runId">Optional run filter (unused in slice 1).</param>
    /// <param name="workItemId">Optional work-item filter (unused in slice 1).</param>
    /// <param name="cancellationToken"></param>
    public static Task<IReadOnlyList<VisualArtifact>> FetchAsync(
        IVisualArtifactStore store,
        ProjectId projectId,
        Guid? runId,
        Guid? workItemId,
        CancellationToken cancellationToken)
    {
        // The store API intentionally does not expose a "list" method
        // for slice 1 — the run artifacts controller has its own
        // list, and the visual-artifact surface ships with content +
        // a placeholder list that the dashboard wires once slice 3
        // lands. Until then we synthesise an empty page (the dashboard
        // knows to render "no artifacts yet").
        return Task.FromResult<IReadOnlyList<VisualArtifact>>([]);
    }

    /// <summary>Maps persistence rows onto the wire view-model.</summary>
    /// <param name="artifacts"></param>
    public static IReadOnlyList<VisualArtifactListItem> Project(IReadOnlyList<VisualArtifact> artifacts)
    {
        return [.. artifacts
            .Select(static artifact => new VisualArtifactListItem(
                Id: artifact.Id,
                Filename: artifact.Filename,
                ContentType: artifact.ContentType,
                SizeBytes: artifact.SizeBytes,
                Title: artifact.Title,
                CreatedAt: artifact.CreatedAt,
                CreatedBy: artifact.CreatedBy,
                RunId: artifact.RunId,
                WorkItemId: artifact.WorkItemId,
                SessionId: artifact.SessionId,
                TicketId: artifact.TicketId,
                Version: artifact.Version))];
    }
}
