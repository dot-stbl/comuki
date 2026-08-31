using System.Threading.Channels;
using Comuki.Shared.Contracts.Grpc;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Translator.Grpc;

/// <summary>
/// Client side of the worker bidi stream: events out (Start / Activity /
/// Report), orchestrator commands in (Stop / InjectContext / LeaseExpired).
/// One session per claimed work item; completing the event stream ends the
/// call — the worker's "I'm done" signal.
/// </summary>
/// <remarks>
/// The gRPC response is consumed by exactly ONE background pump into a
/// command channel: keeping a stored <c>IAsyncEnumerator</c> and calling
/// <c>MoveNextAsync</c>/<c>DisposeAsync</c> from different places throws
/// <c>NotSupportedException</c> (grpc-net forbids dispose with a pending
/// move). <see cref="TryReceiveAsync"/> therefore reads from the channel,
/// never from the wire enumerator.
/// </remarks>
public sealed class WorkerSession : IAsyncDisposable
{
    private readonly Channel<WorkerEvent> events;
    private readonly Channel<OrchestratorCommand?> commands;
    private readonly Task commandPump;

    private WorkerSession(Channel<WorkerEvent> events, Channel<OrchestratorCommand?> commands, Task commandPump)
    {
        this.events = events;
        this.commands = commands;
        this.commandPump = commandPump;
    }

    /// <summary>
    /// Opens the stream: authenticates with the worker token via gRPC
    /// metadata and starts pumping commands into an internal channel.
    /// The call reaches the server on the first send or receive.
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

        var commands = Channel.CreateUnbounded<OrchestratorCommand?>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var commandPump = PumpCommandsAsync(service.Connect(events.Reader.ReadAllAsync(cancellationToken), callContext), commands, cancellationToken);
        return new WorkerSession(events, commands, commandPump);
    }

    /// <summary>Queues one worker event onto the stream.</summary>
    /// <param name="workerEvent"></param>
    /// <param name="cancellationToken"></param>
    public ValueTask SendAsync(WorkerEvent workerEvent, CancellationToken cancellationToken = default)
    {
        return events.Writer.WriteAsync(workerEvent, cancellationToken);
    }

    /// <summary>
    /// Waits for the next orchestrator command; null when the stream ended
    /// (server closed or the session was disposed).
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async ValueTask<OrchestratorCommand?> TryReceiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await commands.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // the pump completed the channel: "no more commands", not a fault
            return null;
        }
    }

    /// <summary>
    /// Completes the event stream (ends the call server-side), drains the
    /// command pump and closes the command channel.
    /// </summary>
    public async Task CloseAsync()
    {
        events.Writer.TryComplete();
        try
        {
            await commandPump;
        }
        catch (OperationCanceledException)
        {
            // the session was cancelled mid-stream; the pump's cancellation
            // is the expected close path
        }
        finally
        {
            commands.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }

    private static async Task PumpCommandsAsync(
        IAsyncEnumerable<OrchestratorCommand> response,
        Channel<OrchestratorCommand?> commands,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in response.WithCancellation(cancellationToken))
            {
                await commands.Writer.WriteAsync(command, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // surface as a normal channel completion, not a fault
        }
        catch (RpcException)
        {
            // the server dropped the stream (host shutdown, auth revoke) —
            // the worker sees it as "no more commands"
        }
        finally
        {
            commands.Writer.TryComplete();
        }
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
