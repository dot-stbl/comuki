using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Filtering;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Runs.Controllers;

/// <summary>
/// Run listing surface: paged, filterable and sortable through the filter DSL
/// (grammar on <see cref="FilterQuery"/>). Subject-scope filtered by the
/// orchestration context query filters — out-of-scope rows are absent, not 403.
/// </summary>
/// <param name="runs"></param>
[ApiController]
[Route(ApiRoutes.Runs)]
[RequiresPermission("run:read")]
public sealed class RunsController(RunsListHandler runs) : ControllerBase
{
    /// <summary>
    /// Lists runs with optional <c>filter</c> and <c>sort</c> DSL expressions
    /// (e.g. <c>status==running</c>, <c>createdAt&gt;=now(-7d)</c>,
    /// <c>sort=updatedAt,desc</c>). Filterable fields: <c>Status</c>
    /// (eq/in/notIn), <c>CreatedAt</c> + <c>UpdatedAt</c> (range, now()).
    /// </summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [ProducesResponseType<RunsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> ListAsync([FromQuery] FilterQuery query, CancellationToken cancellationToken = default)
    {
        return RunsEndpointRunner.ExecuteAsync(
            async () => new OkObjectResult(await runs.ListAsync(query, cancellationToken)));
    }
}

/// <summary>Typed exceptions → ProblemDetails — one place for the runs surface.</summary>
public static class RunsEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the filter DSL's parse failures.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (FilterParseException exception)
        {
            return RunsProblems.InvalidFilter(exception.Message);
        }
    }
}

/// <summary>Problem results of the runs surface (same shape as the chat/auth surfaces).</summary>
public static class RunsProblems
{
    /// <summary>400 for an unparseable or illegal filter/sort expression.</summary>
    /// <param name="detail"></param>
    public static ActionResult InvalidFilter(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid filter expression",
            Detail = detail,
        };
        problem.Extensions["code"] = "filter.invalid";

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
