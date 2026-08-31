namespace Comuki.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Claim names the Identity module writes and reads. The stock claim
/// types cover subject/email/name; the two module-specific values ride
/// under a <c>comuki_</c> prefix.
/// </summary>
public static class IdentityClaimNames
{
    /// <summary>The user's security stamp (<c>tokens_version</c>) embedded in login cookies.</summary>
    public const string TokensVersion = "comuki_tokens_version";

    /// <summary>The api key id behind an API-key principal.</summary>
    public const string ApiKeyId = "comuki_api_key_id";
}
