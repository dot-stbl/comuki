namespace Comuki.Host.Workers.Api;

/// <summary>Failure body: human-readable reason text plus the claimed generation.</summary>
/// <param name="Reason"></param>
/// <param name="Generation">The generation the caller claimed this item under — a mismatch is treated as an ownership miss.</param>
public sealed record FailWorkItemRequest(string Reason, int Generation);
