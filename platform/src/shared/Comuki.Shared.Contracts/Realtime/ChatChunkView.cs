namespace Comuki.Shared.Contracts.Realtime;

/// <summary>
/// One brain progress fragment broadcast to the <c>chat:{id}</c> group while
/// a turn is running. Ephemeral by contract: the fragment already lives in
/// the transcript's thinking part once the turn settles, so a connection
/// that misses chunks loses nothing durable — the next read catches up.
/// </summary>
/// <param name="SessionId">The session whose turn is streaming.</param>
/// <param name="Seq">The brain's own chunk sequence number, for diagnostics.</param>
/// <param name="Text">The progress fragment, verbatim.</param>
[RealtimeContract]
public sealed record ChatChunkView(Guid SessionId, int Seq, string Text);
