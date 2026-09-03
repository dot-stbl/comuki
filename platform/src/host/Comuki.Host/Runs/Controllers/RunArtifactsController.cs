using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Contracts.Artifacts;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Runs.Controllers;

/// <summary>
/// Run artifact bundle surface: returns the immutable artifact pointers
/// the host-composed packager wrote to MinIO for one run. The list is
/// empty when the run has not been bundled yet (still in flight, or
/// the packager has not yet observed the terminal transition). The
/// permission is <c>run:read</c> (same as the parent RunsController)
/// — artifact pointers reveal the same surface the run events already
/// surface; the artifact bodies themselves are gated on signed URLs
/// the FE fetches per object on demand.
/// </summary>
/// <param name="store">MinIO-backed artifact store.</param>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts")]
[RequiresPermission("run:read")]
public sealed class RunArtifactsController(IRunArtifactStore store) : ControllerBase
{
    /// <summary>
    /// Returns the artifact pointers for one run, empty when the bundle
    /// has not been packaged yet. URIs are the canonical MinIO object
    /// URLs; the FE uses them as named links.
    /// </summary>
    /// <param name="projectId">Owning project (path).</param>
    /// <param name="runId">Run whose artifacts are requested (path).</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [EndpointName("runs-artifacts")]
    [ProducesResponseType<RunArtifactsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunArtifactsPage>> ListAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var pointers = await store.ListAsync(
            new ProjectId(projectId),
            new RunId(runId),
            cancellationToken);
        return Ok(new RunArtifactsPage(pointers, projectId, runId));
    }
}

/// <summary>One page of run-artifact pointers — wraps <see cref="ArtifactPointer"/> with the project/run id echo.</summary>
/// <param name="Items">Bundle objects, empty when the run has not been packaged yet.</param>
/// <param name="ProjectId">Owning project id.</param>
/// <param name="RunId">Run id the bundle belongs to.</param>
public sealed record RunArtifactsPage(
    IReadOnlyList<ArtifactPointer> Items,
    Guid ProjectId,
    Guid RunId);
