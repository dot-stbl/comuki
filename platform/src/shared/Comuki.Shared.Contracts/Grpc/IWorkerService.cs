using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Comuki.Shared.Contracts.Grpc;

/// <summary>
/// The bidirectional worker stream between <c>Comuki.Host.Translator</c>
/// (the worker container entrypoint) and the Orchestrator (the Host):
/// the worker pushes <see cref="WorkerEvent"/>s (StageStart, Activity
/// chunks, final StageReport) as pi emits them; the orchestrator
/// interleaves <see cref="OrchestratorCommand"/>s (Stop, InjectContext,
/// LeaseExpired).
/// </summary>
/// <remarks>
/// Contract-first protobuf-net.Grpc: no <c>.proto</c>, descriptors are
/// built at runtime from these attributes. The worker authenticates with
/// its opaque worker token in the <c>authorization</c> gRPC metadata
/// header; the server maps it back to the WorkerId the token was issued
/// for. Duplex shape: the request stream is the <c>events</c> parameter,
/// the response stream is the return value.
/// </remarks>
[Service]
public interface IWorkerService
{
    /// <summary>
    /// Opens the bidi stream. Events flow worker → orchestrator; commands
    /// flow orchestrator → worker. The call ends when the worker completes
    /// its events enumeration (process shutdown).
    /// </summary>
    /// <param name="events"></param>
    /// <param name="context"></param>
    [Operation]
    public IAsyncEnumerable<OrchestratorCommand> Connect(IAsyncEnumerable<WorkerEvent> events, CallContext context = default);
}
