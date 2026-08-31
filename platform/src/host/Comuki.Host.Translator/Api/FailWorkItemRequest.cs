namespace Comuki.Host.Translator.Api;

/// <summary>Failure body: human-readable reason text.</summary>
/// <param name="Reason"></param>
public sealed record FailWorkItemRequest(string Reason);
