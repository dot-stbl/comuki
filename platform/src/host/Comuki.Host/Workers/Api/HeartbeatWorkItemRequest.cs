namespace Comuki.Host.Workers.Api;

/// <summary>Heartbeat body: the generation the caller claimed this item under.</summary>
/// <param name="Generation">A mismatch (the owning Run has since been cancelled/superseded) is treated as an ownership miss, same as today's owner/lease guard.</param>
public sealed record HeartbeatWorkItemRequest(int Generation);
