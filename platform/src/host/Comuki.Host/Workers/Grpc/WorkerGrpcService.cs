using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Workers.Grpc;

/// <summary>
/// Server side of the worker bidi stream (T3.2): authenticates the worker
/// token from gRPC metadata, links the stream to its WorkerId, forwards
/// every <see cref="WorkerEvent"/> to the run journal and pumps
/// <see cref="OrchestratorCommand"/>s (Stop / InjectContext / LeaseExpired)
/// from the <see cref="WorkerCommandHub"/> down to the worker. One stream
/// per connection; the scope lives for the whole stream.
/// </summary>
/// <param name="authenticator"></param>
/// <param name="scopeAccessor"></param>
/// <param name="commandHub"></param>
/// <param name="journal"></param>
/// <param name="clock"></param>
/// <param name="loggerFactory"></param>
/// <param name="logger"></param>
public sealed class WorkerGrpcService(
    WorkerTokenAuthenticator authenticator,
    ISubjectScopeAccessor scopeAccessor,
    WorkerCommandHub commandHub,
    IRunJournal journal,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<WorkerGrpcService> logger) : IWorkerService
{
    /// <inheritdoc />
    public async IAsyncEnumerable<OrchestratorCommand> Connect(
        IAsyncEnumerable<WorkerEvent> events,
        CallContext context)
    {
        var workerId = WorkerGrpcAuthentication.AuthenticateOrThrow(authenticator, context);
        logger.LogInformation("Worker {WorkerId} stream opened", workerId.Value);

        // The stream is a platform-system consumer for its whole life:
        // journal writes flow for runs of every project, and the
        // subject-scope query filters must not confine them.
        using var systemScope = scopeAccessor.AsSystem("worker-runtime");

        var commands = commandHub.Register(workerId);
        var streamJournal = new WorkerStreamJournal(
            journal,
            clock,
            loggerFactory.CreateLogger<WorkerStreamJournal>());

        // Stream semantics (contract doc): the call ends when the worker
        // completes its events enumeration. Commands flow while events are
        // still coming; the pump cancels the command loop on completion —
        // no pending MoveNextAsync is ever left for dispose (grpc-net
        // throws NotSupportedException on dispose-with-pending-move).
        var endStream = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var ender = WorkerStreamEnd.WhenPumpedEndAsync(
            WorkerEventStreamPump.PumpAsync(events, streamJournal, context.CancellationToken),
            endStream);

        var commandReader = commands.Reader.ReadAllAsync(endStream.Token).GetAsyncEnumerator(endStream.Token);
        try
        {
            while (true)
            {
                bool hasCommand;
                try
                {
                    hasCommand = await commandReader.MoveNextAsync();
                }
                catch (OperationCanceledException) when (endStream.IsCancellationRequested)
                {
                    // the events pump finished and cancelled the command
                    // loop — the documented close path, not a fault
                    break;
                }

                if (!hasCommand)
                {
                    break;
                }

                yield return commandReader.Current;
            }
        }
        finally
        {
            await commandReader.DisposeAsync();

            // awaiting the ender is awaiting the pump: cancellation of the
            // worker stream is swallowed (expected close), any real pump
            // fault propagates from here. Must run BEFORE endStream.Dispose()
            // — the ender's finally still cancels the token.
            await ender;
            endStream.Dispose();
            commandHub.Unregister(workerId, commands);
            logger.LogInformation("Worker {WorkerId} stream closed", workerId.Value);
        }
    }
}

/// <summary>Token gate of the gRPC stream: valid token or the call dies unauthenticated.</summary>
file static class WorkerGrpcAuthentication
{
    public static WorkerId AuthenticateOrThrow(WorkerTokenAuthenticator authenticator, CallContext context)
    {
        return authenticator.Authenticate(WorkerTokenHeaders.TryGetFromGrpc(context.RequestHeaders))
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "unknown or expired worker token"));
    }
}

/// <summary>Consumes the worker's event stream into the journal until the stream ends or is cancelled.</summary>
file static class WorkerEventStreamPump
{
    public static async Task PumpAsync(
        IAsyncEnumerable<WorkerEvent> events,
        WorkerStreamJournal streamJournal,
        CancellationToken cancellationToken)
    {
        await foreach (var workerEvent in events.WithCancellation(cancellationToken))
        {
            await streamJournal.AppendAsync(workerEvent, cancellationToken);
        }
    }
}

/// <summary>
/// Cancels the command loop as soon as the events pump finishes (worker
/// completed its stream) and republishes the pump's completion: stream
/// cancellation is swallowed (expected close), real faults propagate.
/// </summary>
file static class WorkerStreamEnd
{
    public static async Task WhenPumpedEndAsync(Task pump, CancellationTokenSource endStream)
    {
        try
        {
            await pump;
        }
        catch (OperationCanceledException)
        {
            // the worker dropped the stream mid-events; expected close path
        }
        finally
        {
            await endStream.CancelAsync();
        }
    }
}
