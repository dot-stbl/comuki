using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-tool permission map for <c>POST /api/v1/mcp</c> (security audit
/// A01-1). The dispatcher is the single chokepoint for every JSON-RPC
/// tool call, so the gate lives here — the global MVC permission filter
/// only sees the MCP endpoint as one route and cannot discriminate by
/// tool name. A missing entry is a deny: tools not listed here are
/// rejected before the handler runs.
/// </summary>
internal static class McpToolPermissionMap
{
    private static readonly PermissionKey knowledgeRead = new("knowledge:read");

    private static readonly PermissionKey knowledgeWrite = new("knowledge:write");

    private static readonly PermissionKey runRead = new("run:read");

    private static readonly Dictionary<string, PermissionKey> requiredByTool = new(StringComparer.Ordinal)
    {
        ["knowledge.search"] = knowledgeRead,
        ["knowledge.ingest"] = knowledgeWrite,
        ["runs.list"] = runRead,
        ["runs.get"] = runRead,
    };

    /// <summary>
    /// True when the subject is allowed to invoke the named tool. An
    /// anonymous subject (<paramref name="subject"/> null) is denied
    /// outright — the endpoint accepts cookie / api-key auth but never
    /// answers anonymous callers.
    /// </summary>
    /// <param name="toolName">The JSON-RPC tool name (e.g. <c>runs.list</c>).</param>
    /// <param name="subject">Caller resolved from the cookie / api-key principal.</param>
    /// <param name="evaluator">Permission evaluator (cached, 30s TTL).</param>
    /// <param name="cancellationToken"></param>
    public static async ValueTask<bool> IsAllowedAsync(
        string toolName,
        RoleSubject? subject,
        IPermissionEvaluator evaluator,
        CancellationToken cancellationToken = default)
    {
        var resolved = subject;

        if (resolved is null)
        {
            return false;
        }

        if (!requiredByTool.TryGetValue(toolName, out var required))
        {
            return false;
        }

        var authorization = await evaluator.EvaluateAsync(resolved.Value, cancellationToken);

        return authorization.IsPermitted(required);
    }

    /// <summary>Stable failure code surfaced in the JSON-RPC error <c>message</c>; clients branch on this string.</summary>
    public const string PermissionDeniedCode = "permission.denied";
}
