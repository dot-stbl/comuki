namespace Comuki.Host.Workers.Api;

/// <summary>
/// Claim request body: the labels the worker presents (from its
/// <c>COMUKI_*</c> environment). The claiming worker's id comes from its
/// bearer token — never from the body.
/// </summary>
/// <param name="Image"></param>
/// <param name="ProfilesRef"></param>
/// <param name="ProfileKey"></param>
public sealed record ClaimWorkItemRequest(string Image, string ProfilesRef, string ProfileKey);
