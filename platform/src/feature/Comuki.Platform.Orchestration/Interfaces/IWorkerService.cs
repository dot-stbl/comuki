using Comuki.Platform.Orchestration.Grpc;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Comuki.Platform.Orchestration.Interfaces;

/// <summary>
/// Contract for the bidirectional stream between <c>Comuki.Platform.Worker.Translator</c>
/// (the worker process) and the Orchestrator (the control plane).
///
/// Why this lives in the Orchestration feature and not in the Worker project:
/// the Orchestrator owns the protocol — it defines what workers can say to it and
/// what it can say back. The Worker is a consumer. Putting the contract in the
/// Orchestration feature library keeps a single source of truth, avoids a circular
/// layer reference (feature/ cannot depend on application/), and lets both sides
/// (Worker.Translator implementing it, Orchestrator's gRPC client consuming it)
/// reference the same types.
///
/// No <c>.proto</c> file: contract is defined as a C# interface with
/// <c>[Service]</c>, gRPC service descriptors are generated at runtime via
/// <c>protobuf-net.Grpc</c> reflection. See
/// <c>.soly/docs/architecture/comuki-architecture.md</c> § 03
/// (Управляющий цикл) and the gRPC transport rationale in
/// <c>comuki-decisions.md</c> § "Транспорты по природе шва".
/// </summary>
/// <remarks>
/// <c>protobuf-net.Grpc</c> 1.2.2 contract model:
/// <list type="bullet">
///   <item><c>[Service]</c> on an interface (CS0592 explicitly forbids class)</item>
///   <item><c>[Operation]</c> on each method (replaces v3.x's <c>[OperationContract]</c>)</item>
///   <item>Runtime reads attributes via reflection and emits gRPC descriptors
///         on the server (via <c>ServerBinder</c>) and on the client (via
///         <c>ClientFactory</c>). No protoc, no MSBuild codegen, no .proto.</item>
/// </list>
/// Concrete implementation lands in <c>Comuki.Platform.Worker.Translator</c> at 04-04.
/// </remarks>
[Service]
public interface IWorkerService
{
    /// <summary>
    /// Bidi stream: the worker pushes <see cref="WorkerEvent"/>s as <c>pi</c> emits them
    /// (StageStart, StageActivity chunks, final StageReport); the Orchestrator can
    /// interleave <see cref="OrchestratorCommand"/>s (Stop).
    /// Real gRPC handshake wires in 04-04.
    /// </summary>
    [Operation]
    public ValueTask Connect(
        IAsyncEnumerable<WorkerEvent> events,
        IAsyncEnumerable<OrchestratorCommand> commands,
        CallContext context = default);
}
