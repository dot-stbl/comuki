namespace Comuki.Host.Translator.Api.Models.Responses;

/// <summary>A claimed work item. Lease deadline is UTC unix milliseconds on the wire.</summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
/// <param name="LeaseUntilUnixMs"></param>
/// <param name="Attempt"></param>
public sealed record ClaimedWorkItemResponse(
    Guid WorkItemId,
    Guid RunId,
    string ProfileKey,
    string Brief,
    long LeaseUntilUnixMs,
    int Attempt);
