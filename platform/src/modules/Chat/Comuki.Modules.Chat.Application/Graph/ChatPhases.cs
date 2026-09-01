namespace Comuki.Modules.Chat.Application.Graph;

/// <summary>Phase values the router and confirm nodes write to <see cref="ChatChannels.Phase"/>.</summary>
public static class ChatPhases
{
    /// <summary>Ask a clarifying question instead of calling the brain.</summary>
    public const string Clarify = "clarify";

    /// <summary>Invoke the brain (chat or plan mode).</summary>
    public const string Think = "think";

    /// <summary>Interrupt for the approve card / route a resume decision.</summary>
    public const string Confirm = "confirm";

    /// <summary>Apply the approved plan (queue the run).</summary>
    public const string Act = "act";

    /// <summary>Turn finished.</summary>
    public const string Done = "done";

    /// <summary>/init onboarding wizard node.</summary>
    public const string Init = "init";
}
