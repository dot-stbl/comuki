using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Domain.Learning;

namespace Comuki.Host.Errors.Handlers.Learning;

// Wire rows for the Learning surface's domain errors, taken verbatim from
// the retired per-module runner (Learning) arm they replace: status, title,
// detail sentence, the code spelling and the decision-conflict extensions. Only
// the `type` URN changed spelling — it is now derived from the code
// (design D4) instead of being absent from the row.

/// <summary>Decision on an already-decided candidate → 409 with <c>candidateId</c> + <c>currentStatus</c>.</summary>
internal sealed class LearningDecisionConflictProblemHandler()
    : ProblemHandler<LearningDecisionConflictException>(StatusCodes.Status409Conflict, "Learning candidate already decided")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(LearningDecisionConflictException exception)
    {
        return Row(
            exception.Message,
            exception.Code,
            new Dictionary<string, object?>
            {
                ["candidateId"] = exception.CandidateId.Value.ToString(),
                ["currentStatus"] = LearningStatusKeys.Key(exception.Current),
            });
    }
}
