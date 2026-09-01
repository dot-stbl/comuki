namespace Comuki.Modules.Chat.Application.Slash;

/// <summary>
/// One slash command as listed to the dashboard / autocomplete and expanded
/// into the brain task by the router.
/// </summary>
/// <param name="Key">Command identity without the slash (e.g. <c>init</c>).</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Description">One-line description.</param>
/// <param name="Body">Full instruction body expanded into the brain task (empty for graph-native commands like <c>init</c>).</param>
/// <param name="Source">Where the command comes from: <c>builtin</c> or <c>control-plane</c>.</param>
public sealed record ChatSlashCommand(
    string Key,
    string Name,
    string Description,
    string Body,
    string Source);
