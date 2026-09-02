using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Application.Tickets;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Intake.Controllers;

/// <summary>Typed exceptions → ProblemDetails — one place for every intake endpoint.</summary>
public static class IntakeEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the intake module's typed exceptions.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (IntakeTicketNotFoundException exception)
        {
            return IntakeProblems.Problem(
                StatusCodes.Status404NotFound,
                "intake.ticket_not_found",
                "Intake ticket not found",
                exception.Message);
        }
        catch (SourceConnectionNotFoundException exception)
        {
            return IntakeProblems.Problem(
                StatusCodes.Status404NotFound,
                "intake.connection_not_found",
                "Source connection not found",
                exception.Message);
        }
        catch (AdmissionRuleNotFoundException exception)
        {
            return IntakeProblems.Problem(
                StatusCodes.Status404NotFound,
                "intake.rule_not_found",
                "Admission rule not found",
                exception.Message);
        }
        catch (IntakeTicketConflictException)
        {
            return IntakeProblems.Problem(
                StatusCodes.Status409Conflict,
                "intake.ticket_conflict",
                "Ticket not claimable",
                "the ticket already has an active run");
        }
        catch (ValidationException exception)
        {
            return IntakeProblems.Validation(IntakeValidationErrors.Of(exception));
        }
    }
}

/// <summary>Problem results shared by the intake controllers (same shape as the auth surface).</summary>
public static class IntakeProblems
{
    /// <summary>Typed problem result.</summary>
    /// <param name="statusCode"></param>
    /// <param name="code"></param>
    /// <param name="title"></param>
    /// <param name="detail"></param>
    public static ActionResult Problem(int statusCode, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
        };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>Validation problem result.</summary>
    /// <param name="errors">Field → messages.</param>
    public static ActionResult Validation(IReadOnlyDictionary<string, string[]> errors)
    {
        return new ObjectResult(new ValidationProblemDetails(errors.ToDictionary()))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>FluentValidation failures → field dictionary.</summary>
file static class IntakeValidationErrors
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
