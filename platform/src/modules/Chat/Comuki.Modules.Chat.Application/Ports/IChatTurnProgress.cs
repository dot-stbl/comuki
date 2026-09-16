using Comuki.Modules.Chat.Domain.Ids;

namespace Comuki.Modules.Chat.Application.Ports;

/// <summary>How one running chat turn ended. Wire-safe labels, spelled lowercase on the hub.</summary>
public enum ChatTurnDone
{
    /// <summary>The turn journaled its reply; the transcript is authoritative.</summary>
    Replied,

    /// <summary>The thread interrupted on the approve card and waits for a decision.</summary>
    AwaitingApproval,

    /// <summary>The turn threw; nothing was journaled and the overlay must not linger.</summary>
    Failed,
}

/// <summary>
/// Live progress of a running chat turn, fanned out to whoever watches the
/// session (the SignalR <c>chat:{id}</c> group on the host). Pushed while
/// the turn driver is still in flight — the journal stays the audit record,
/// this port is the ephemeral "watching it think" channel. Best-effort by
/// contract: an implementation that cannot deliver logs and moves on;
/// progress must never fail the turn.
/// </summary>
public interface IChatTurnProgress
{
    /// <summary>One progress fragment of the running turn, in arrival order.</summary>
    /// <param name="sessionId">The session the turn runs on.</param>
    /// <param name="seq">The brain's own chunk sequence number.</param>
    /// <param name="text">The fragment, verbatim.</param>
    /// <param name="cancellationToken"></param>
    public Task ChunkAsync(ChatSessionId sessionId, int seq, string text, CancellationToken cancellationToken = default);

    /// <summary>Terminal signal of the turn — success, interrupt or failure.</summary>
    /// <param name="sessionId">The session the turn ran on.</param>
    /// <param name="done">How it ended.</param>
    /// <param name="cancellationToken"></param>
    public Task DoneAsync(ChatSessionId sessionId, ChatTurnDone done, CancellationToken cancellationToken = default);
}
