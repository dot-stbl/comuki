namespace Comuki.Modules.Chat.Domain.Messages;

/// <summary>
/// Message role. Closed set: user turns, assistant replies, system journal
/// entries (memory digests fed to the brain — audit per the memory contract)
/// and tool observations.
/// </summary>
public enum ChatMessageRole
{
    /// <summary>Message from the human subject.</summary>
    User = 0,

    /// <summary>Reply of the chat harness.</summary>
    Assistant = 1,

    /// <summary>Journal entry (memory digest fed to the brain, audit).</summary>
    System = 2,

    /// <summary>Tool observation (runs list, plan apply result).</summary>
    Tool = 3,
}
