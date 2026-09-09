namespace Comuki.Host.Workers.Api;

/// <summary>
/// Typed-result helpers for the worker-runtime endpoints. These produce
/// <see cref="IResult"/> ProblemDetails responses for the documented
/// error codes (<c>worker.unauthenticated</c>, <c>work-item.not-owner</c>).
/// Extracted from <see cref="WorkerEndpoints"/> so the endpoint class
/// holds only orchestration and no inline result factories.
/// </summary>
internal static class WorkerResults
{
    /// <summary>401 ProblemDetails — the worker token is missing or invalid.</summary>
    public static IResult Unauthenticated()
    {
        return TypedResults.Problem(
            title: "Worker authentication failed",
            detail: "present a valid worker token in the Authorization header",
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = "worker.unauthenticated" });
    }

    /// <summary>409 ProblemDetails — the work-item is unknown, not running, or not leased to this worker.</summary>
    public static IResult NotOwner()
    {
        return TypedResults.Problem(
            title: "Work item not owned",
            detail: "the item is unknown, not running, or not leased to this worker (the lease may have expired)",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = "work-item.not-owner" });
    }
}
