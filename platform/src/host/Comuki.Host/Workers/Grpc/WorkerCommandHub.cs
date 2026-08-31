using System.Collections.Concurrent;
using System.Threading.Channels;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers.Grpc;

/// <summary>
/// Outbound command surface for anything in the orchestrator that needs to
/// reach a connected worker mid-run (chat Stop, context inject, lease
/// reaper). Sends are best-effort: a worker without a live stream is a miss,
/// not an error.
/// </summary>
public interface IWorkerCommandPipe
{
    /// <summary>Sends a soft-stop to a connected worker. False when it has no live stream.</summary>
    /// <param name="workerId"></param>
    /// <param name="reason"></param>
    public bool TrySendStop(WorkerId workerId, string reason);

    /// <summary>Sends a mid-run context injection. False when the worker has no live stream.</summary>
    /// <param name="workerId"></param>
    /// <param name="context"></param>
    public bool TrySendInjectContext(WorkerId workerId, string context);

    /// <summary>Tells a worker its lease expired and ownership is gone. False when no live stream.</summary>
    /// <param name="workerId"></param>
    public bool TrySendLeaseExpired(WorkerId workerId);
}

/// <summary>
/// Registry of live worker streams: one command channel per connected
/// <see cref="WorkerId"/>. The gRPC service registers on stream open and
/// unregisters on close; the pipe side writes into the channel, the service
/// pumps it to the wire.
/// </summary>
public sealed class WorkerCommandHub() : IWorkerCommandPipe
{
    private readonly ConcurrentDictionary<WorkerId, Channel<OrchestratorCommand>> channelsByWorker = new();

    /// <summary>Registers a fresh command channel for the worker, replacing any previous one.</summary>
    /// <param name="workerId"></param>
    public Channel<OrchestratorCommand> Register(WorkerId workerId)
    {
        var channel = Channel.CreateBounded<OrchestratorCommand>(
            new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = false });

        if (channelsByWorker.TryRemove(workerId, out var previous))
        {
            _ = previous.Writer.TryComplete();
        }

        channelsByWorker[workerId] = channel;
        return channel;
    }

    /// <summary>Unregisters the worker's channel and completes it. No-op when absent or replaced.</summary>
    /// <param name="workerId"></param>
    /// <param name="channel"></param>
    public void Unregister(WorkerId workerId, Channel<OrchestratorCommand> channel)
    {
        if (channelsByWorker.TryRemove(new KeyValuePair<WorkerId, Channel<OrchestratorCommand>>(workerId, channel)))
        {
            _ = channel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public bool TrySendStop(WorkerId workerId, string reason)
    {
        return WorkerCommandHubWriters.TryWriteTo(channelsByWorker, workerId, new OrchestratorCommand { Stop = new Stop { Reason = reason } });
    }

    /// <inheritdoc />
    public bool TrySendInjectContext(WorkerId workerId, string context)
    {
        return WorkerCommandHubWriters.TryWriteTo(
            channelsByWorker,
            workerId,
            new OrchestratorCommand { InjectContext = new InjectContext { Context = context } });
    }

    /// <inheritdoc />
    public bool TrySendLeaseExpired(WorkerId workerId)
    {
        return WorkerCommandHubWriters.TryWriteTo(channelsByWorker, workerId, new OrchestratorCommand { LeaseExpired = new LeaseExpired() });
    }
}

/// <summary>Channel lookup + write for the hub's send surface.</summary>
file static class WorkerCommandHubWriters
{
    public static bool TryWriteTo(
        ConcurrentDictionary<WorkerId, Channel<OrchestratorCommand>> channels,
        WorkerId workerId,
        OrchestratorCommand command)
    {
        return channels.TryGetValue(workerId, out var channel) && channel.Writer.TryWrite(command);
    }
}
