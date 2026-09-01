namespace Comuki.Modules.Chat.Application.Slash;

/// <summary>
/// Graph-native slash commands — the only commands the chat graph handles
/// without the brain deciding how. <c>/init</c> drives the onboarding wizard
/// node; <c>/help</c> lists the merged catalog.
/// </summary>
public static class ChatSlashBuiltins
{
    /// <summary>Key of the onboarding wizard command.</summary>
    public const string InitKey = "init";

    /// <summary>Key of the help command.</summary>
    public const string HelpKey = "help";

    /// <summary>Every built-in, ordered by key.</summary>
    public static readonly IReadOnlyList<ChatSlashCommand> All =
    [
        new ChatSlashCommand(
            HelpKey,
            "Help",
            "List every available chat command.",
            "List the available slash commands with one line each, then ask what to do next.",
            ChatSlashSources.Builtin),
        new ChatSlashCommand(
            InitKey,
            "Initialize workspace",
            "Run the onboarding wizard (repo, compute, models, knowledge).",
            string.Empty,
            ChatSlashSources.Builtin),
    ];
}
