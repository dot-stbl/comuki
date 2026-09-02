namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Claim request body: the labels this worker presents (from its COMUKI_* environment).</summary>
/// <param name="Image"></param>
/// <param name="ProfilesRef"></param>
/// <param name="ProfileKey"></param>
public sealed record ClaimWorkItemRequest(string Image, string ProfilesRef, string ProfileKey);
