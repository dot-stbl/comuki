namespace Comuki.Shared.Contracts.Queue;

/// <summary>
/// Claim labels a worker presents when claiming: the item must match all
/// three (mirrors the container's image digest, the pinned profiles git ref
/// and the profile the worker was scaled for).
/// </summary>
/// <param name="Image"></param>
/// <param name="ProfilesRef"></param>
/// <param name="ProfileKey"></param>
public sealed record WorkItemLabels(string Image, string ProfilesRef, string ProfileKey);
