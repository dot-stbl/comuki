using System.Collections.Frozen;

namespace Comuki.Host.Mcp;

/// <summary>
/// The worker-side twin of <see cref="McpToolPermissionMap"/>: the exact
/// tool set a worker-token caller may invoke. Workers get the
/// project-bound read/write surface (memory + knowledge retrieval) and
/// nothing else — no runs surface, no ingestion, no free-project scoping
/// (the project is forced server-side from their lease). A tool not listed
/// here, or a worker with no active work item (null project), is denied
/// before the handler runs.
/// </summary>
internal static class McpWorkerToolGate
{
    private static readonly FrozenSet<string> workerTools = new[]
    {
        "memory.recall",
        "memory.note",
        "knowledge.search",
        "learning.suggest",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// True when the worker may invoke the named tool: the tool is in the
    /// worker set and the worker holds an active work item (a resolvable
    /// project scope).
    /// </summary>
    /// <param name="toolName">The JSON-RPC tool name (e.g. <c>memory.recall</c>).</param>
    /// <param name="worker">The authenticated worker caller.</param>
    public static bool IsAllowed(string toolName, McpWorkerCaller worker)
    {
        return workerTools.Contains(toolName) && worker.ProjectId is not null;
    }
}
