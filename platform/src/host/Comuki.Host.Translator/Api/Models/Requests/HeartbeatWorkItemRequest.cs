namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Heartbeat body: the generation this worker claimed the item under.</summary>
public sealed record HeartbeatWorkItemRequest(int Generation);
