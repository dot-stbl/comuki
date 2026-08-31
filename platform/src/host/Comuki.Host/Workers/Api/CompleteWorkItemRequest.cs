namespace Comuki.Host.Workers.Api;

/// <summary>Completion body: the worker-produced result JSON (must be valid, non-empty JSON).</summary>
/// <param name="ResultJson"></param>
public sealed record CompleteWorkItemRequest(string ResultJson);
