using Comuki.Shared.Contracts.Brain;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Chat.Brain;

/// <summary>
/// The real brain port: a code-first protobuf-net client over
/// <see cref="IBrainService"/>. <c>Think</c> is server-streaming, so
/// <see cref="StreamAsync"/> yields chunks the moment the brain produces
/// them; <see cref="InvokeAsync"/> drains the same stream into one
/// <see cref="BrainReply"/> for callers that only want the aggregate.
/// </summary>
/// <param name="brain">Client proxy over the brain channel.</param>
/// <param name="logger">Diagnostics for a failing brain call.</param>
public sealed class BrainGrpcClient(IBrainService brain, ILogger<BrainGrpcClient> logger) : IBrainClient
{
    /// <inheritdoc />
    public async Task<BrainReply> InvokeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Brain call started ({Kind})", request.Kind);

        var reply = await BrainReply.AggregateAsync(StreamAsync(request, cancellationToken), cancellationToken);

        logger.LogInformation(
            "Brain call finished ({Kind}, {ChunkCount} progress chunks)",
            request.Kind,
            reply.Chunks.Count);

        return reply;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BrainChunk> StreamAsync(
        BrainRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var context = new CallContext(new CallOptions(cancellationToken: cancellationToken));

        // The proxy call itself can fail before the first move (an unroutable
        // channel, a dead endpoint); the enumeration moves are wrapped below
        // for the same reason — an iterator cannot yield inside try/catch,
        // so the translation rides on the manual moves.
        var stream = brain.Think(request, context);

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            BrainChunk chunk;

            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                chunk = enumerator.Current;
            }
            catch (RpcException exception)
            {
                throw new BrainUnavailableException(
                    exception.StatusCode.ToString(),
                    "the brain host did not answer this turn (" + exception.StatusCode + ")",
                    exception);
            }

            yield return chunk;
        }
    }
}
