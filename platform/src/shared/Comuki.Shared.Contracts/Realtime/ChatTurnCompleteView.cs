namespace Comuki.Shared.Contracts.Realtime;

/// <summary>
/// Terminal signal of one chat turn, broadcast to the <c>chat:{id}</c>
/// group: the streaming phase is over and the journal is authoritative.
/// Clients clear their live overlay and refresh the transcript; the payload
/// is the outcome word, not the turn result — the POST response and the
/// transcript endpoint remain the sources of truth.
/// </summary>
/// <param name="SessionId">The session whose turn finished.</param>
/// <param name="Outcome">Lowercase outcome: replied | awaiting_approval | failed.</param>
[RealtimeContract]
public sealed record ChatTurnCompleteView(Guid SessionId, string Outcome);
