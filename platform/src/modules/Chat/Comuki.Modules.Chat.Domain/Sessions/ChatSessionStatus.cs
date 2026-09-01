namespace Comuki.Modules.Chat.Domain.Sessions;

/// <summary>
/// Chat session lifecycle. Closed set: a session is active until the
/// 30-day-inactivity archive sweep (memory contract) soft-archives it.
/// </summary>
public enum ChatSessionStatus
{
    /// <summary>The session accepts messages.</summary>
    Active = 0,

    /// <summary>Archived after inactivity; kept for history, rejects new turns.</summary>
    Archived = 1,
}
