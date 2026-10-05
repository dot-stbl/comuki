using Comuki.Host.Runs;
using Comuki.Shared.Filtering.Parser;

namespace Comuki.Host.Errors.Handlers.Runs;

// Wire rows for the Runs surface's typed errors, taken verbatim from the
// retired per-module runner (Runs) arms they replace: status, title, detail sentences,
// the code spellings and the decision-conflict extensions. Only the `type`
// URN changed spelling — it is now derived from the code (design D4)
// instead of being absent from the row.

/// <summary>Decision on a run in the wrong source status → 409 with <c>currentStatus</c> + <c>decision</c>.</summary>
internal sealed class RunDecisionConflictProblemHandler()
    : ProblemHandler<RunDecisionConflictException>(StatusCodes.Status409Conflict, "Run state conflict")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(RunDecisionConflictException exception)
    {
        return Row(
            exception.Message,
            exception.Code,
            new Dictionary<string, object?>
            {
                ["currentStatus"] = exception.Current.ToString(),
                ["decision"] = exception.Decision,
            });
    }
}

/// <summary>Steer on a terminal run → 409 with <c>currentStatus</c> + the steer-specific code (add-orchestra §1 — Baton, Phase 1a).</summary>
internal sealed class RunNotRunningForSteerProblemHandler()
    : ProblemHandler<RunNotRunningForSteerException>(StatusCodes.Status409Conflict, "Run not running")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(RunNotRunningForSteerException exception)
    {
        return Row(
            exception.Message,
            exception.Code,
            new Dictionary<string, object?>
            {
                ["currentStatus"] = exception.Current.ToString(),
            });
    }
}

/// <summary>Filter/sort DSL failed to parse → 400 (the caller's query string, not a server fault).</summary>
internal sealed class FilterParseProblemHandler()
    : ProblemHandler<FilterParseException>(StatusCodes.Status400BadRequest, "Invalid filter expression")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(FilterParseException exception)
    {
        return Row(exception.Message, "filter.invalid");
    }
}
