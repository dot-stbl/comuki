namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// Port to the brain process (Comuki.Host.Brain). Implemented in-process by
/// a stub for dev/tests when no brain process is configured; the real
/// implementation talks to the brain host over gRPC (same shape as the
/// Translator channel). Callers journal what they fed the brain.
/// </summary>
public interface IBrainClient
{
    /// <summary>Invokes the brain agent-loop once and returns chunks + final payload.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public Task<BrainReply> InvokeAsync(BrainRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes the brain and yields its chunks as they arrive — progress
    /// fragments first, the final chunk (with <see cref="BrainChunk.IsFinal"/>
    /// and <see cref="BrainChunk.FinalJson"/>) last. Callers that only need
    /// the aggregate use <see cref="InvokeAsync"/>; callers that surface the
    /// fragments live (the chat turn stream) read this.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public IAsyncEnumerable<BrainChunk> StreamAsync(BrainRequest request, CancellationToken cancellationToken = default);
}
