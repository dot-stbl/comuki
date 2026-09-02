using Comuki.Host.Intake.Models;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>
/// Admission rule CRUD — the per-project watch/inbox configuration the
/// webhook pipeline consults. Reads demand <c>intake:read</c>; writes
/// demand <c>source:write</c>.
/// </summary>
/// <param name="rules"></param>
[ApiController]
[Route(ApiRoutes.AdmissionRules)]
public sealed class AdmissionRulesController(AdmissionRuleService rules) : ControllerBase
{
    /// <summary>Lists rules, optionally per project.</summary>
    /// <param name="projectId">Optional project filter.</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<IReadOnlyList<AdmissionRuleView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdmissionRuleView>>> ListAsync(
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await rules.ListAsync(
            projectId is { } value ? new ProjectId(value) : null,
            cancellationToken));
    }

    /// <summary>Creates a rule.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost]
    [RequiresPermission("source:write")]
    [ProducesResponseType<AdmissionRuleView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateAsync(CreateAdmissionRuleRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
        {
            var view = await rules.CreateAsync(
                new CreateAdmissionRuleCommand(
                    new ProjectId(request.ProjectId),
                    request.Mode,
                    request.FilterJson),
                cancellationToken);

            return new CreatedResult(ApiRoutes.AdmissionRules + "/" + view.Id, view);
        });
    }

    /// <summary>Reads one rule.</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{ruleId:guid}")]
    [RequiresPermission("intake:read")]
    [ProducesResponseType<AdmissionRuleView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> GetAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await rules.GetAsync(new AdmissionRuleId(ruleId), cancellationToken)));
    }

    /// <summary>Partial update (PATCH semantics — null fields stay).</summary>
    /// <param name="ruleId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPut("{ruleId:guid}")]
    [RequiresPermission("source:write")]
    [ProducesResponseType<AdmissionRuleView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateAsync(Guid ruleId, UpdateAdmissionRuleRequest request, CancellationToken cancellationToken = default)
    {
        return IntakeEndpointRunner.ExecuteAsync(async () =>
            Ok(await rules.UpdateAsync(
                new AdmissionRuleId(ruleId),
                request.Mode,
                request.FilterJson,
                request.Enabled,
                cancellationToken)));
    }

    /// <summary>Deletes a rule (idempotent).</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    [HttpDelete("{ruleId:guid}")]
    [RequiresPermission("source:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await rules.DeleteAsync(new AdmissionRuleId(ruleId), cancellationToken);
        return NoContent();
    }
}
