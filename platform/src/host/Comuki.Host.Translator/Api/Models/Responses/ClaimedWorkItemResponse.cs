namespace Comuki.Host.Translator.Api.Models.Responses;

/// <summary>A claimed work item. Lease deadline is UTC unix milliseconds on the wire.</summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProjectId">Project the parent run belongs to — the worker's project context.</param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
/// <param name="LeaseUntilUnixMs"></param>
/// <param name="Attempt"></param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL; <c>null</c> when the orchestrator mints no key.</param>
/// <param name="VirtualKey">Minted proxy bearer token expiring with the lease; <c>null</c> when the orchestrator mints no key.</param>
public sealed record ClaimedWorkItemResponse(
    Guid WorkItemId,
    Guid RunId,
    Guid ProjectId,
    string ProfileKey,
    string Brief,
    long LeaseUntilUnixMs,
    int Attempt,
    string? ProxyBaseUrl = null,
    string? VirtualKey = null);
