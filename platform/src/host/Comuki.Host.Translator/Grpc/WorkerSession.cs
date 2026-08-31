using System.Threading.Channels;
using Comuki.Shared.Contracts.Grpc;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Translator.Grpc;

/// <summary>
/// Client side of the worker bidi stream: events out (Start / Activity /
/// Report), orchestrator commands in (Stop / InjectContext / LeaseExpired).
/// One session per claimed work item; closing the event stream ends the
/// call, which is the worker's "I'm done" signal.
/// </summary>
public sealed class WorkerSession : IAsyncDisposable
{
    private readonly Channel<WorkerEvent> events;
    private readonly IAsyncEnumerator<OrchestratorCommand> commands;

    private WorkerSession(Channel<WorkerEvent> events, IAsyncEnumerator<OrchestratorCommand> commands)
    {
        this.events = events;
        this.commands = commands;
    }

    /// <summary>
    /// Opens the stream: authenticates with the worker token via gRPC
    /// metadata and starts pulling commands. The call reaches the server
    /// on the first send or receive.
    /// </summary>
    /// <param name="service"></param>
    /// <param name="workerToken"></param>
    /// <param name="cancellationToken"></param>
    public static WorkerSession Open(IWorkerService service, string workerToken, CancellationToken cancellationToken = default)
    {
        var events = Channel.CreateUnbounded<WorkerEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var callContext = new CallContext(new CallOptions(new Metadata
        {
            { WorkerSessionHeaders.AuthorizationKey, WorkerSessionHeaders.NormalizeToken(workerToken) },
        }));
        var commands = service
            .Connect(events.Reader.ReadAllAsync(cancellationToken), callContext)
            .GetAsyncEnumerator(cancellationToken);
        return new WorkerSession(events, commands);
    }

    /// <summary>Queues one worker event onto the stream.</summary>
    /// <param name="workerEvent"></param>
    /// <param name="cancellationToken"></param>
    public ValueTask SendAsync(WorkerEvent workerEvent, CancellationToken cancellationToken = default)
    {
        return events.Writer.WriteAsync(workerEvent, cancellationToken);
    }

    /// <summary>Waits for the next orchestrator command; null when the stream ended.</summary>
    /// <param name="cancellationToken"></param>
    public async ValueTask<OrchestratorCommand?> TryReceiveAsync(CancellationToken cancellationToken = default)
    {
        // The protobuf-net command stream's enumerator takes no token, so the
        // token cancels the *wait*: stopping the host unsticks this loop
        // instead of leaving it parked on a command that never comes.
        return await commands.MoveNextAsync().AsTask().WaitAsync(cancellationToken)
            ? commands.Current
            : null;
    }

    /// <summary>Completes the event stream (ends the call server-side) and disposes the response stream.</summary>
    public ValueTask CloseAsync()
    {
        events.Writer.TryComplete();
        return commands.DisposeAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return CloseAsync();
    }
}

/// <summary>gRPC metadata keys and token normalization.</summary>
internal static class WorkerSessionHeaders
{
    public const string AuthorizationKey = "authorization";

    public static string NormalizeToken(string workerToken)
    {
        return workerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? workerToken["Bearer ".Length..]
            : workerToken;
    }
}
