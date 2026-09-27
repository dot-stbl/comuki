using Comuki.Modules.Scheduler.Domain.Jobs;

namespace Comuki.Host.Errors.Handlers.Scheduler;

// Wire rows for the Scheduler module's domain errors, taken verbatim from the
// retired per-module runner (Scheduler) arms they replace: status, title, detail sentences
// and the code spellings. Only the `type` URN changed spelling — it is now
// derived from the code (design D4) instead of being absent from the row.

/// <summary>Scheduled job row absent → 404.</summary>
internal sealed class ScheduledJobNotFoundProblemHandler()
    : ProblemHandler<ScheduledJobNotFoundException>(StatusCodes.Status404NotFound, "Scheduled job not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ScheduledJobNotFoundException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}

/// <summary>Cron expression failed to parse → 400 (the caller's payload, not a server fault).</summary>
internal sealed class InvalidCronExpressionProblemHandler()
    : ProblemHandler<InvalidCronExpressionException>(StatusCodes.Status400BadRequest, "Invalid cron expression")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(InvalidCronExpressionException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}
