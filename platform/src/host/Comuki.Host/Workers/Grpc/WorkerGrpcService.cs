using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
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
/// <param name="commandHub"></param>
/// <param name="journal"></param>
/// <param name="clock"></param>
/// <param name="loggerFactory"></param>
/// <param name="logger"></param>
public sealed class WorkerGrpcService(
    WorkerTokenAuthenticator authenticator,
    WorkerCommandHub commandHub,
    IRunJournal journal,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    ILogger<WorkerGrpcService> logger) : IWorkerService
{
    /// <inheritdoc />
    public async IAsyncEnumerable<OrchestratorCommand> Connect(
        IAsyncEnumerable<WorkerEvent> events,
        CallContext context = default)
    {
        var workerId = WorkerGrpcAuthentication.AuthenticateOrThrow(authenticator, context);
        logger.LogInformation("Worker {WorkerId} stream opened", workerId.Value);

        var commands = commandHub.Register(workerId);
        var streamJournal = new WorkerStreamJournal(
            journal,
            clock,
            loggerFactory.CreateLogger<WorkerStreamJournal>());
        var pump = WorkerEventStreamPump.PumpAsync(events, streamJournal, context.CancellationToken);

        try
        {
            await foreach (var command in commands.Reader.ReadAllAsync(context.CancellationToken))
            {
                yield return command;
            }
        }
        finally
        {
            commandHub.Unregister(workerId, commands);
            await pump;
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
