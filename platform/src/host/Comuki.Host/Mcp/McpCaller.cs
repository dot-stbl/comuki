using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Host.Mcp;

/// <summary>
/// The resolved caller of one MCP dispatch: a dashboard / API-key subject
/// (<see cref="Subject"/>), a swarm worker (<see cref="Worker"/>), or
/// neither (<see cref="Anonymous"/> — denied every tool). Exactly one of
/// the two is non-null for an authenticated caller; the endpoint resolves
/// the caller once per request before handing it to
/// <see cref="McpServer.DispatchAsync"/>, so no handler ever re-reads
/// <c>HttpContext</c>.
/// </summary>
/// <param name="Subject">Cookie / API-key principal; gated by <see cref="McpToolPermissionMap"/>.</param>
/// <param name="Worker">Worker-token principal; gated by <see cref="McpWorkerToolGate"/>.</param>
public sealed record McpCaller(RoleSubject? Subject = null, McpWorkerCaller? Worker = null)
{
    /// <summary>The deny-everything caller — no subject, no worker token.</summary>
    public static McpCaller Anonymous { get; } = new();
}
