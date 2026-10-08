using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Chat.Controllers;

/// <summary>
/// The 404 row of the chat value flow — a session the resolver did not
/// find (absent or foreign) is a result, not a thrown exception, so the
/// row stays endpoint-local per the domain-error-contract scope: only
/// exception→ProblemDetails mapping consolidates. Same shape the retired
/// <c>ChatProblems</c> always shipped.
/// </summary>
public static class ChatProblems
{
    /// <summary>404 for an unknown (or foreign) session.</summary>
    public static ActionResult NotFound(Guid sessionId)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
        var typed = TypedResults.Problem(
            title: "Chat session not found",
            detail: $"chat session '{sessionId}' not found",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = "chat.session_not_found" });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
