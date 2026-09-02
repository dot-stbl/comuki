namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Completion body: the worker-produced result JSON.</summary>
/// <param name="ResultJson"></param>
public sealed record CompleteWorkItemRequest(string ResultJson);
