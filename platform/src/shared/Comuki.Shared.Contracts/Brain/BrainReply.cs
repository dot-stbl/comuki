namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// Result of one brain invocation: the streamed progress fragments (already
/// materialized — callers that want them live read
/// <see cref="IBrainClient.StreamAsync"/> instead) and the final payload. For
/// <c>plan</c> invocations <paramref name="FinalJson"/> is the plan JSON
/// validated by <see cref="Plans.PlanValidator"/>.
/// </summary>
/// <param name="Chunks">Progress fragments in arrival order; may be empty.</param>
/// <param name="FinalJson">Final payload (plan JSON for <c>plan</c>, reply text for <c>chat</c>).</param>
public sealed record BrainReply(IReadOnlyList<string> Chunks, string FinalJson)
{
    // canon judgement #11: AggregateAsync on a contracts record is deliberate — a static factory that dedupes the chunk-draining half every IBrainClient.InvokeAsync implementation shares.
    /// <summary>
    /// Aggregates a chunk stream into one reply — the draining half every
    /// <see cref="IBrainClient.InvokeAsync"/> implementation shares. Progress
    /// fragments keep their arrival order; the last final chunk's payload
    /// wins. Empty progress texts are dropped the same way the journal drops
    /// them, so the aggregate cannot contain a blank fragment.
    /// </summary>
    /// <param name="stream">The chunk stream of one invocation.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<BrainReply> AggregateAsync(
        IAsyncEnumerable<BrainChunk> stream,
        CancellationToken cancellationToken = default)
    {
        List<string> chunks = [];
        var finalJson = string.Empty;

        await foreach (var chunk in stream.WithCancellation(cancellationToken))
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

        return new BrainReply(chunks, finalJson);
    }
}
