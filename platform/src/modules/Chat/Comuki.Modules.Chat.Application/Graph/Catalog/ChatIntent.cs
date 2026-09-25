namespace Comuki.Modules.Chat.Application.Graph.Catalog;

/// <summary>
/// Deterministic v1 intent heuristic the router uses to pick
/// clarify/think and the brain invocation mode. Deliberately dumb and
/// testable — the brain replaces it as the router once the brain host is
/// wired for routing too.
/// </summary>
public static class ChatIntent
{
    /// <summary>Window scanned for an imperative verb — «In the repository
    /// working directory, edit hello.txt» carries its verb on the fifth
    /// word.</summary>
    public const int VerbWindow = 6;

    /// <summary>Words that mark a message as a question/conversation even when a
    /// task verb appears later («what should we build next?» is chat, not a plan).</summary>
    public static readonly IReadOnlySet<string> QuestionOpeners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "what", "why", "how", "who", "when", "where", "which",
        "can", "could", "should", "would", "is", "are", "do", "does", "did", "will",
    };

    /// <summary>Imperative verbs that mark a message as a task to plan rather than conversation.</summary>
    public static readonly IReadOnlySet<string> TaskVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "add", "append", "bump", "build", "change", "configure", "create", "delete", "deploy",
        "edit", "fix", "implement", "make", "migrate", "move", "plan", "refactor", "remove",
        "rename", "restart", "set", "setup", "update", "write",
    };

    /// <summary>Whether the message reads like a task request: an imperative
    /// verb within the first words, and not a question opener first.</summary>
    /// <param name="message">Raw user message.</param>
    public static bool LooksLikeTask(string message)
    {
        var words = message
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(VerbWindow)
            .Select(static word => word.Trim(['.', ',', ';', ':', '!', '?']))
            .ToArray();

        return words.Length > 0
            && !QuestionOpeners.Contains(words[0])
            && words.Any(TaskVerbs.Contains);
    }
}
