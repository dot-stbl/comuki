using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// Server side of the brain invocation surface: <c>Comuki.Host.Brain</c>
/// serves it; the Host chat graph and orchestration auto-replan call it
/// through the mirrored client contract. Server-streaming: progress
/// chunks first, the final chunk last.
/// </summary>
/// <remarks>
/// Contract-first protobuf-net.Grpc: no <c>.proto</c>, descriptors are
/// built at runtime from these attributes — the same recipe as the worker
/// protocol (<see cref="Grpc.IWorkerService"/>).
/// </remarks>
[Service]
public interface IBrainService
{
    /// <summary>
    /// Runs one brain agent loop and streams its progress; the last chunk
    /// carries the final JSON. The call fails with
    /// <c>StatusCode.InvalidArgument</c> on an unknown kind and
    /// <c>StatusCode.Internal</c> when the loop cannot produce a result
    /// (invalid plan after retry, iteration cap, model unavailable).
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    [Operation]
    public IAsyncEnumerable<BrainChunk> Think(BrainRequest request, CallContext context);
}
