using Comuki.Host.Translator.Api;
using Comuki.Host.Translator.Grpc;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// State of one claimed work item: the claim, the open gRPC session and
/// the cancellation that a Stop / LeaseExpired command (or process
/// shutdown) trips to kill pi.
/// </summary>
/// <param name="claimed"></param>
/// <param name="session"></param>
public sealed class WorkerRun(
    ClaimedWorkItemResponse claimed,
    WorkerSession session) : IAsyncDisposable
{
    /// <summary>
    /// boundary: linked to the hosted-service stop; cancelled by Stop/LeaseExpired commands
    /// </summary>
    public required CancellationTokenSource RunCancellation { get; init; }

    /// <summary>The claimed item this run executes.</summary>
    public ClaimedWorkItemResponse Claimed => claimed;

    /// <summary>The worker bidi stream the run reports over.</summary>
    public WorkerSession Session => session;

    /// <summary>Set when the orchestrator said the lease expired — completion must be skipped.</summary>
    public bool LeaseLost { get; set; }

    /// <summary>Set when the orchestrator sent a Stop command.</summary>
    public bool StopRequested { get; set; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        RunCancellation.Dispose();
        return session.DisposeAsync();
    }
}
