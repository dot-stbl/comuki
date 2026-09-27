using Comuki.Host.Errors;
using Comuki.Host.Errors.Handlers.Scheduler;
using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Modules.Scheduler.Domain.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// Wire rows for the Scheduler module's domain errors (domain-error-contract
/// task 4.1). Every literal below — status, type URN, title, detail sentence
/// and code — is pinned from the <c>SchedulerEndpointRunner</c> arms these
/// handlers replace, so the runner deletion cannot drift from the shape the
/// FE already consumes. The one deliberate delta: the <c>type</c> URN is new
/// (the runner set none) and derived from the code (design D4).
/// </summary>
public sealed class SchedulerProblemHandlersShould
{
    [Fact(DisplayName = "Given an unknown job id, when the JobNotFound handler answers, then the row pins the runner's 404 shape")]
    public void ScheduledJobNotFoundPinsRunnerShape()
    {
        var jobId = new ScheduledJobId(Guid.Parse("0d1c3a57-8e2b-4f18-9d4a-6b7e5c2a1f00"));
        var handler = new ScheduledJobNotFoundProblemHandler();

        var answer = handler.Answer(new ScheduledJobNotFoundException(jobId));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Type.ShouldBe("urn:comuki:error:scheduler.job_not_found");
        answer.Title.ShouldBe("Scheduled job not found");
        answer.Detail.ShouldBe($"scheduled job {jobId} not found");
        answer.Code.ShouldBe("scheduler.job_not_found");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("scheduler.job_not_found");
        answer.ValidationErrors.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unparseable cron expression, when the InvalidCron handler answers, then the row pins the runner's 400 shape")]
    public void InvalidCronExpressionPinsRunnerShape()
    {
        var handler = new InvalidCronExpressionProblemHandler();

        var answer = handler.Answer(new InvalidCronExpressionException("every monday", "cron expression 'every monday' is not a 5-field expression"));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.Type.ShouldBe("urn:comuki:error:scheduler.invalid_cron");
        answer.Title.ShouldBe("Invalid cron expression");
        answer.Detail.ShouldBe("cron expression 'every monday' is not a 5-field expression");
        answer.Code.ShouldBe("scheduler.invalid_cron");
        answer.Extensions.Count.ShouldBe(1);
        answer.Extensions["code"].ShouldBe("scheduler.invalid_cron");
    }

    [Fact(DisplayName = "Given the Scheduler rows registered, when a ScheduledJobNotFoundException resolves, then the registry serves the module row over the 422 default")]
    public void SchedulerRowsResolveThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry(
        [
            new ScheduledJobNotFoundProblemHandler(),
            new InvalidCronExpressionProblemHandler(),
        ],
        NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new ScheduledJobNotFoundException(new ScheduledJobId(Guid.NewGuid())));

        answer.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        answer.Title.ShouldBe("Scheduled job not found");
    }
}
