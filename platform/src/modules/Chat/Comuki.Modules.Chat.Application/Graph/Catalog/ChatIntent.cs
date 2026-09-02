namespace Comuki.Modules.Chat.Application.Graph.Catalog;

/// <summary>
/// Deterministic v1 intent heuristic the router uses to pick
/// clarify/think and the brain invocation mode. Deliberately dumb and
/// testable — the brain replaces it as the router once the brain host is
/// wired for routing too.
/// </summary>
public static class ChatIntent
{
    /// <summary>First words that mark a message as a task to plan rather than conversation.</summary>
    public static readonly IReadOnlySet<string> TaskVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "add", "build", "create", "deploy", "fix", "implement", "make", "plan", "refactor", "restart", "set", "setup", "write",
    };

    /// <summary>Whether the message reads like a task request (imperative verb first).</summary>
    /// <param name="message">Raw user message.</param>
    public static bool LooksLikeTask(string message)
    {
        var firstWord = message.Trim().Split(' ', 2)[0];
        return TaskVerbs.Contains(firstWord);
    }
}
