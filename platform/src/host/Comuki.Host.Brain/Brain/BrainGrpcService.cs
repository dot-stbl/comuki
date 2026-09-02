using System.Runtime.CompilerServices;
using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Shared.Contracts.Brain;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Server side of the brain surface: validates the request kind, runs the
/// agent loop and streams its chunks. Loop failures map to gRPC faults —
/// invalid argument for a bad kind or empty task, internal for an
/// invalid-after-retry plan, an exhausted loop or an unconfigured model.
/// </summary>
/// <param name="agent"></param>
/// <param name="logger"></param>
public sealed class BrainGrpcService(BrainAgent agent, ILogger<BrainGrpcService> logger) : IBrainService
{
    /// <inheritdoc />
    public async IAsyncEnumerable<BrainChunk> Think(BrainRequest request, CallContext context)
    {
        if (BrainRequestKindKeys.Parse(request.Kind) is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"unknown brain request kind '{request.Kind}' — expected plan|brief|repair|answer"));
        }

        if (string.IsNullOrWhiteSpace(request.Task))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "brain request task must not be empty"));
        }

        logger.LogInformation(
            "Brain think started ({Kind}, task {TaskLength} chars)",
            request.Kind,
            request.Task.Length);

        await foreach (var chunk in BrainFaultMapping
            .StreamAsync(agent, request, context.CancellationToken)
            .WithCancellation(context.CancellationToken))
        {
            yield return chunk;
        }

        logger.LogInformation("Brain think finished ({Kind})", request.Kind);
    }
}

/// <summary>
/// Wraps the agent stream so brain loop faults surface as gRPC statuses
/// (the IBrainService contract). An iterator cannot yield inside
/// try/catch, so the mapping rides on the enumerator moves instead.
/// </summary>
file static class BrainFaultMapping
{
    public static async IAsyncEnumerable<BrainChunk> StreamAsync(
        BrainAgent agent,
        BrainRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = agent.RunAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            BrainChunk? next = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                next = enumerator.Current;
            }
            catch (BrainInvalidPlanException exception)
            {
                throw new RpcException(new Status(StatusCode.Internal, exception.Message));
            }
            catch (BrainExhaustedException exception)
            {
                throw new RpcException(new Status(StatusCode.Internal, exception.Message));
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("brain model is not configured"))
            {
                throw new RpcException(new Status(StatusCode.Internal, exception.Message));
            }

            if (next is { } chunk)
            {
                yield return chunk;
            }
        }
    }
}
