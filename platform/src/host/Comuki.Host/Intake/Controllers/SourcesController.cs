using Comuki.Host.Intake.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>
/// Source connection CRUD: the tracker bindings with their webhook
/// routing keys and env-ref secrets. Reads demand <c>intake:read</c>;
/// writes demand <c>source:write</c>. The probe endpoints (#41, #42)
/// share the same prefix and answer a stable shape regardless of the
/// upstream's success / failure — a rejected credential is a result,
/// not a 5xx.
/// </summary>
/// <param name="connections"></param>
/// <param name="rules"></param>
/// <param name="probe"></param>
[ApiController]
public sealed class SourcesController(
    SourceConnectionService connections,
    AdmissionRuleService rules,
    SourceProbeService probe) : ControllerBase
{
    /// <summary>Lists connections, optionally per project.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet(ApiRoutes.Sources)]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<IReadOnlyList<SourceConnectionView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SourceConnectionView>>> ListAsync(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await connections.ListAsync(
            projectId is { } value ? new ProjectId(value) : null,
            cancellationToken));
    }

    /// <summary>Creates a connection; the view carries the webhook path to configure in the tracker.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost(ApiRoutes.Sources)]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateAsync(CreateSourceConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
        {
            var view = await connections.CreateAsync(
                new CreateSourceConnectionCommand(
                    new ProjectId(request.ProjectId),
                    request.Provider,
                    request.Name,
                    request.SettingsJson,
                    request.SecretEnvRef),
                cancellationToken);

            return new CreatedResult(ApiRoutes.Sources + "/" + view.Id, view);
        });
    }

    /// <summary>Reads one connection.</summary>
    /// <param name="sourceId"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet(ApiRoutes.Source)]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> GetAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await connections.GetAsync(new SourceConnectionId(sourceId), cancellationToken)));
    }

    /// <summary>Partial update (PATCH semantics — null fields stay).</summary>
    /// <param name="sourceId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPut(ApiRoutes.Source)]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceConnectionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateAsync(Guid sourceId, UpdateSourceConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await connections.UpdateAsync(
                new SourceConnectionId(sourceId),
                request.Name,
                request.SettingsJson,
                request.SecretEnvRef,
                request.Enabled,
                cancellationToken)));
    }

    /// <summary>Deletes a connection (idempotent).</summary>
    /// <param name="sourceId"></param>
    /// <param name="cancellationToken"></param>
    [HttpDelete(ApiRoutes.Source)]
    [RequiresPermission("source:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        await connections.DeleteAsync(new SourceConnectionId(sourceId), cancellationToken);
        return NoContent();
    }

    /// <summary>Probes a draft source connection before save (issue #41).</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost(ApiRoutes.SourcesProbeDraft)]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceProbeResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SourceProbeResult>> ProbeDraftAsync(
        [FromBody] ProbeSourceDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await probe.ProbeDraftAsync(
            request.Provider,
            request.SettingsJson,
            request.SecretEnvRef,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Probes an existing source connection (issue #42).</summary>
    /// <param name="sourceId"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost(ApiRoutes.SourceProbe)]
    [RequiresPermission("source:write")]
    [ProducesResponseType<SourceProbeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SourceProbeResult>> ProbeConnectionAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var connection = await connections.GetAsync(new SourceConnectionId(sourceId), cancellationToken);

        // The view's stored shape carries the persisted credentials; the
        // probe service resolves the secret through ISecretResolver at
        // call time so the operator never sees the resolved value.
        var result = await probe.ProbeDraftAsync(
            connection.Provider,
            connection.SettingsJson,
            connection.SecretEnvRef,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Partial update of an admission rule nested under a source connection
    /// (issue #40). Wire-compatible with the sibling
    /// <c>PUT /api/v1/admission-rules/{ruleId}</c> — the source id is
    /// accepted in the route for the FE's nested form, but the rule row
    /// lives in its own table and is matched by id alone.
    /// </summary>
    /// <param name="sourceId">Source connection id (route context, not used to filter the rule lookup).</param>
    /// <param name="ruleId">The admission rule id.</param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPut(ApiRoutes.SourceAdmissionRule)]
    [RequiresPermission("source:write")]
    [ProducesResponseType<AdmissionRuleView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateRuleUnderSourceAsync(
        Guid sourceId,
        Guid ruleId,
        UpdateAdmissionRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        // `sourceId` is part of the route template
        // (ApiRoutes.SourceAdmissionRule = "/api/v1/sources/{sourceId:guid}/rules/{ruleId:guid}")
        // so the FE's nested form resolves to the right controller action; the
        // admission-rule update keys off ruleId alone. Reserved if a future
        // cross-source consistency check (e.g. "the rule's filter must mention
        // the source's namespace") ever needs the source row.
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await rules.UpdateAsync(
                new AdmissionRuleId(ruleId),
                request.Mode,
                request.FilterJson,
                request.Enabled,
                cancellationToken)));
    }
}
