using Comuki.Host.Chat.Brain;
using Comuki.Modules.Chat.Application.Sessions;

namespace Comuki.Host.Errors.Handlers.Chat;

// Wire rows for the Chat surface's typed errors, taken verbatim from the
// retired per-module runner (Chat) arms they replace: status, title, detail sentences
// and the code spellings. Only the `type` URN changed spelling — it is now
// derived from the code (design D4) instead of being absent from the row.

/// <summary>Session interrupted on a plan approve → 409 until the decision lands.</summary>
internal sealed class ChatApprovePendingProblemHandler()
    : ProblemHandler<ChatApprovePendingException>(StatusCodes.Status409Conflict, "Plan approval pending")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ChatApprovePendingException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>
/// Brain call did not complete → honest 503. The row's code is the fixed
/// <see cref="BrainUnavailableException.ProblemCode"/>, not the gRPC status
/// name the exception carries — the FE branches on the former.
/// </summary>
internal sealed class BrainUnavailableProblemHandler()
    : ProblemHandler<BrainUnavailableException>(StatusCodes.Status503ServiceUnavailable, "Brain unavailable")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(BrainUnavailableException exception)
    {
        return Row(exception.Message, BrainUnavailableException.ProblemCode);
    }
}
