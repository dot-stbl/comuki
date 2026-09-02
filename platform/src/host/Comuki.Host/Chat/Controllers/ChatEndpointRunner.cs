using Comuki.Modules.Chat.Application.Sessions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Chat.Controllers;

/// <summary>Typed exceptions → ProblemDetails — one place for every chat endpoint.</summary>
public static class ChatEndpointRunner
{
    /// <summary>Runs one endpoint body, mapping the chat module's typed exceptions.</summary>
    /// <param name="action">Endpoint body.</param>
    public static async Task<ActionResult> ExecuteAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ChatApprovePendingException exception)
        {
            return ChatProblems.Problem(
                StatusCodes.Status409Conflict,
                "chat.approve_pending",
                "Plan approval pending",
                exception.Message);
        }
        catch (ValidationException exception)
        {
            return ChatProblems.Validation(ChatValidationErrors.Of(exception));
        }
    }
}

/// <summary>Problem results shared by the chat controllers (same shape as the auth surface).</summary>
public static class ChatProblems
{
    /// <summary>404 for an unknown (or foreign) session.</summary>
    /// <param name="sessionId"></param>
    public static ActionResult NotFound(Guid sessionId)
    {
        return Problem(
            StatusCodes.Status404NotFound,
            "chat.session_not_found",
            "Chat session not found",
            $"chat session '{sessionId}' not found");
    }

    /// <summary>Typed problem result.</summary>
    /// <param name="statusCode"></param>
    /// <param name="code"></param>
    /// <param name="title"></param>
    /// <param name="detail"></param>
    public static ActionResult Problem(int statusCode, string code, string title, string detail)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
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
file static class ChatValidationErrors
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
