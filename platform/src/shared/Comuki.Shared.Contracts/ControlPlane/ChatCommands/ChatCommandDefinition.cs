namespace Comuki.Shared.Contracts.ControlPlane.ChatCommands;

/// <summary>One built-in chat command as listed to the chat harness and the dashboard.</summary>
/// <param name="Key">Stable identity: the file stem of the command document (e.g. <c>init</c>); the slash-command name.</param>
/// <param name="Name">Human-readable name from the document frontmatter.</param>
/// <param name="Description">One-line description for command lists and autocomplete.</param>
/// <param name="Body">Markdown instructions the brain follows when the command is invoked.</param>
public sealed record ChatCommandDefinition(
    string Key,
    string Name,
    string Description,
    string Body);
