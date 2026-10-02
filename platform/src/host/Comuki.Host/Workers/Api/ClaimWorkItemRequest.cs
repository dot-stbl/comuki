namespace Comuki.Host.Workers.Api;

/// <summary>
/// Claim request body: the labels the worker presents (from its
/// <c>COMUKI_*</c> environment). The claiming worker's id comes from its
/// bearer token — never from the body.
/// <see cref="EnvClass"/> is REQUIRED (task 3.2): missing or empty is a
/// <c>400 worker.env_class.required</c>; the queue SQL filters
/// <c>env_class = @envClass</c>, so omitting it would silently miss
/// every claim. <see cref="Image"/> is now OPTIONAL in the wire shape —
/// the item's image is pinned by the host at WorkItem.Create from the
/// project default, and the worker doesn't need to know it. Older workers
/// still post image; we keep accepting it and ignore it for the match.
/// </summary>
/// <param name="Image">Present for backward compatibility; ignored on the match.</param>
/// <param name="ProfilesRef"></param>
/// <param name="ProfileKey"></param>
/// <param name="EnvClass"></param>
public sealed record ClaimWorkItemRequest(string? Image, string ProfilesRef, string ProfileKey, string EnvClass);
