namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Failure body: human-readable reason text plus the claimed generation.</summary>
/// <param name="Reason"></param>
/// <param name="Generation"></param>
public sealed record FailWorkItemRequest(string Reason, int Generation);
