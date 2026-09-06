using Comuki.Modules.Scheduler.Domain.Jobs;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Scheduler;

/// <summary>
/// Typed exceptions → ProblemDetails for the scheduler surface. One
/// place for every scheduler endpoint; mirrors
/// <c>IntakeEndpointRunner</c>. <c>ScheduledJobNotFoundException</c>
/// maps to <c>404</c> (canonical REST not-found) — the global
/// <c>ExceptionMapping</c> would otherwise fold it into the
/// generic <c>DomainException → 422</c> arm.
/// </summary>
public static class SchedulerEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the scheduler module's typed exceptions.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ScheduledJobNotFoundException exception)
        {
            return SchedulerProblems.Problem(
                StatusCodes.Status404NotFound,
                "scheduler.job_not_found",
                "Scheduled job not found",
                exception.Message);
        }
        catch (InvalidCronExpressionException exception)
        {
            return SchedulerProblems.Problem(
                StatusCodes.Status400BadRequest,
                "scheduler.invalid_cron",
                "Invalid cron expression",
                exception.Message);
        }
        catch (ValidationException exception)
        {
            return SchedulerProblems.Validation(SchedulerValidationErrors.Of(exception));
        }
    }
}

/// <summary>ProblemResults shared by the scheduler controllers (same shape as the auth surface).</summary>
public static class SchedulerProblems
{
    /// <summary>Typed problem result.</summary>
    /// <param name="statusCode"></param>
    /// <param name="code"></param>
    /// <param name="title"></param>
    /// <param name="detail"></param>
    public static ActionResult Problem(int statusCode, string code, string title, string detail)
    {
        var typed = TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>Validation problem result.</summary>
    /// <param name="errors">Field → messages.</param>
    public static ActionResult Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        var typed = TypedResults.ValidationProblem(errors.ToDictionary());
        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>FluentValidation failures → field dictionary.</summary>
file static class SchedulerValidationErrors
{
    public static IReadOnlyDictionary<string, string[]> Of(ValidationException exception)
    {
        return exception.Errors
            .GroupBy(static failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.Select(static failure => failure.ErrorMessage).ToArray());
    }
}
