using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// A swarm worker calling MCP through the Translator: authenticated by its
/// worker token (the same one the claim loop presents), with the project
/// derived server-side from the work item it currently holds a lease on —
/// never from a client-supplied argument. A worker between items (no
/// running lease) resolves with a null <see cref="ProjectId"/> and is
/// denied every project-bound tool.
/// </summary>
/// <param name="WorkerId">The worker its token was issued for.</param>
/// <param name="ProjectId">
/// Project of the run behind the worker's currently-leased work item; null
/// when the worker holds no running item.
/// </param>
public sealed record McpWorkerCaller(WorkerId WorkerId, Guid? ProjectId);
