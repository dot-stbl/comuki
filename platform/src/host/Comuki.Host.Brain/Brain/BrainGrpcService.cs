using System.Runtime.CompilerServices;
using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Shared.Contracts.Brain;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// Server side of the brain surface: validates the request kind, runs the
/// agent loop and streams its chunks. A plan that stays invalid after its
/// retry degrades to a final answer carrying the validation errors (the
/// user can rephrase and retry) — loop faults other than that map to gRPC
/// faults: invalid argument for a bad kind or empty task, internal for an
/// exhausted loop or an unconfigured model.
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
            .StreamAsync(agent, request, logger, context.CancellationToken)
            .WithCancellation(context.CancellationToken))
        {
            yield return chunk;
        }

        logger.LogInformation("Brain think finished ({Kind})", request.Kind);
    }
}

/// <summary>
/// Wraps the agent stream so brain loop faults surface as gRPC statuses
/// (the IBrainService contract) — except an invalid-after-retry plan,
/// which degrades to a final answer chunk carrying the validation errors.
/// An iterator cannot yield inside try/catch, so the mapping rides on the
/// enumerator moves instead.
/// </summary>
file static class BrainFaultMapping
{
    public static async IAsyncEnumerable<BrainChunk> StreamAsync(
        BrainAgent agent,
        BrainRequest request,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = agent.RunAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var lastSeq = -1;
        while (true)
        {
            BrainChunk? next = null;
            BrainInvalidPlanException? invalidPlan = null;
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
                invalidPlan = exception;
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
                lastSeq = chunk.Seq;
                yield return chunk;
            }

            if (invalidPlan is { } rejected)
            {
                // graceful fallback: a plan that stayed invalid after its
                // retry is a model-quality problem the user can act on —
                // answer with the errors instead of faulting the RPC (the
                // caller would otherwise surface a 503 "brain unavailable")
                logger.LogWarning(
                    "Brain plan stayed invalid after retry ({Kind}, {ErrorCount} validation errors)",
                    request.Kind,
                    rejected.Errors.Count);
                yield return new BrainChunk
                {
                    Seq = lastSeq + 1,
                    IsFinal = true,
                    FinalJson = BrainInvalidPlanReply.Compose(rejected.Errors),
                };
                yield break;
            }
        }
    }
}

/// <summary>Composes the user-facing fallback answer for an invalid-after-retry plan.</summary>
file static class BrainInvalidPlanReply
{
    public static string Compose(IReadOnlyList<string> errors)
    {
        return "Plan invalid — the model's plan stayed malformed after its retry:\n"
            + string.Join("\n", errors.Select(static error => $"- {error}"))
            + "\nNo run was created. Please rephrase the task and try again.";
    }
}
