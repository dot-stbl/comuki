namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Failure body: human-readable reason text.</summary>
/// <param name="Reason"></param>
public sealed record FailWorkItemRequest(string Reason);
