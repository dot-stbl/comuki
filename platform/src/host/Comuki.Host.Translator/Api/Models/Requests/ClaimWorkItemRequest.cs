namespace Comuki.Host.Translator.Api.Models.Requests;

/// <summary>Claim request body: the labels this worker presents (from its COMUKI_* environment).</summary>
/// <param name="Image">Present for backward compatibility; ignored on the match (see <c>Worker.Api.ClaimWorkItemRequest</c>).</param>
/// <param name="ProfilesRef"></param>
/// <param name="ProfileKey"></param>
/// <param name="EnvClass">Environment class this worker was scaled for (COMUKI_ENV_CLASS).</param>
public sealed record ClaimWorkItemRequest(string? Image, string ProfilesRef, string ProfileKey, string EnvClass);
