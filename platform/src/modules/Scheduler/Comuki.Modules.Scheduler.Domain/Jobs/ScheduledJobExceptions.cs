using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Scheduler.Domain.Jobs;

/// <summary>Scheduled job lookup miss.</summary>
/// <param name="id"></param>
public sealed class ScheduledJobNotFoundException(ScheduledJobId id) : DomainException(
    ErrorCode,
    $"scheduled job {id} not found")
{
    private const string ErrorCode = "scheduler.job_not_found";

    /// <summary>The id that was looked up.</summary>
    public ScheduledJobId JobId { get; } = id;
}

/// <summary>Cron expression didn't parse.</summary>
/// <param name="expression"></param>
/// <param name="detail"></param>
public sealed class InvalidCronExpressionException(string expression, string detail) : DomainException(
    ErrorCode,
    detail)
{
    private const string ErrorCode = "scheduler.invalid_cron";

    /// <summary>The expression that failed to parse.</summary>
    public string Expression { get; } = expression;
}
