namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Heartbeat body: the generation this worker claimed the item under.</summary>
/// <param name="Generation"></param>
public sealed record HeartbeatWorkItemRequest(int Generation);
