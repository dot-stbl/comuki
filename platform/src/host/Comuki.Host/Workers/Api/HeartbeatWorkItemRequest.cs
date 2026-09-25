namespace Comuki.Host.Workers.Api;

/// <summary>Heartbeat body: the generation the caller claimed this item under. The
/// endpoint binds this parameter as nullable — a pre-WS4/WS5 Translator that sends
/// no body at all still gets the documented 409 <c>work-item.not-owner</c> instead
/// of a 400 (see <c>WorkerEndpoints.HeartbeatAsync</c>).</summary>
/// <param name="Generation">A mismatch (the owning Run has since been cancelled/superseded) is treated as an ownership miss, same as today's owner/lease guard.</param>
public sealed record HeartbeatWorkItemRequest(int Generation);
