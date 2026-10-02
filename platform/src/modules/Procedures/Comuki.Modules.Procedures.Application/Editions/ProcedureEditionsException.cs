using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Application.Editions;

/// <summary>
/// Typed refusal from the procedure editions gate: a cap was hit or a
/// feature key is not granted. The message names the limit or key.
/// </summary>
public sealed class ProcedureEditionsException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>The project hit its published-procedure cap.</summary>
    public const string PublishedLimitExceeded = "procedures.editions.published_limit_exceeded";

    /// <summary>The project hit its concurrent-pinned-run cap.</summary>
    public const string ConcurrentRunsLimitExceeded = "procedures.editions.concurrent_runs_limit_exceeded";

    /// <summary>Cross-repository binding requires the multi-repo feature key.</summary>
    public const string MultiRepoNotGranted = "procedures.editions.multi_repo_not_granted";

    /// <summary>Edition expired: read-only, no new compilations or runs.</summary>
    public const string EditionExpiredReadOnly = "procedures.editions.expired_read_only";
}
