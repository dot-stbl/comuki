namespace Comuki.Host.Workers.Api;

/// <summary>Completion body: the worker-produced result JSON (must be valid, non-empty JSON) plus the claimed generation.</summary>
/// <param name="ResultJson"></param>
/// <param name="Generation">The generation the caller claimed this item under — a mismatch (the owning Run has since been cancelled/superseded) is treated as an ownership miss.</param>
public sealed record CompleteWorkItemRequest(string ResultJson, int Generation);
