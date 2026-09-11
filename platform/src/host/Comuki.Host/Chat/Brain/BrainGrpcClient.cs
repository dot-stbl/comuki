using Comuki.Shared.Contracts.Brain;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Chat.Brain;

/// <summary>
/// The real brain port: a code-first protobuf-net client over
/// <see cref="IBrainService"/>. <c>Think</c> is server-streaming, so the
/// call drains progress chunks into <see cref="BrainReply.Chunks"/> (they
/// become the turn's thinking part) and keeps the final chunk's payload as
/// <see cref="BrainReply.FinalJson"/>.
/// </summary>
/// <param name="brain">Client proxy over the brain channel.</param>
/// <param name="logger">Diagnostics for a failing brain call.</param>
public sealed class BrainGrpcClient(IBrainService brain, ILogger<BrainGrpcClient> logger) : IBrainClient
{
    /// <inheritdoc />
    public async Task<BrainReply> InvokeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Brain call started ({Kind})", request.Kind);

        var reply = await BrainStreamDraining.DrainAsync(brain, request, cancellationToken);

        logger.LogInformation(
            "Brain call finished ({Kind}, {ChunkCount} progress chunks)",
            request.Kind,
            reply.Chunks.Count);

        return reply;
    }
}

/// <summary>
/// Drains the server stream and translates transport faults. An iterator
/// cannot yield inside try/catch, so the mapping rides on the enumerator
/// moves — the same shape the brain host uses on its side.
/// </summary>
file static class BrainStreamDraining
{
    /// <summary>Consumes the whole stream into one reply.</summary>
    /// <param name="brain">Client proxy.</param>
    /// <param name="request">The invocation.</param>
    /// <param name="cancellationToken">Caller cancellation, flowed into the call.</param>
    /// <exception cref="BrainUnavailableException">The call did not complete.</exception>
    public static async Task<BrainReply> DrainAsync(
        IBrainService brain,
        BrainRequest request,
        CancellationToken cancellationToken)
    {
        List<string> chunks = [];
        var finalJson = string.Empty;
        var context = new CallContext(new CallOptions(cancellationToken: cancellationToken));

        try
        {
            await foreach (var chunk in brain.Think(request, context).WithCancellation(cancellationToken))
            {
                if (chunk.IsFinal)
                {
                    finalJson = chunk.FinalJson;
                }
                else if (chunk.Text.Length > 0)
                {
                    chunks.Add(chunk.Text);
                }
            }
        }
        catch (RpcException exception)
        {
            throw new BrainUnavailableException(
                exception.StatusCode.ToString(),
                "the brain host did not answer this turn (" + exception.StatusCode + ")",
                exception);
        }

        return new BrainReply(chunks, finalJson);
    }
}
