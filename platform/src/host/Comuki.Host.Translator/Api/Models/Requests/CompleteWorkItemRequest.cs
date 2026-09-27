namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Completion body: the worker-produced result JSON plus the claimed generation.</summary>
/// <param name="ResultJson"></param>
/// <param name="Generation"></param>
public sealed record CompleteWorkItemRequest(string ResultJson, int Generation);
