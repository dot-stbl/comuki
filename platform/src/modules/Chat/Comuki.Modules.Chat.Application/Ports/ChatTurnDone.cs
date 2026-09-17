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
