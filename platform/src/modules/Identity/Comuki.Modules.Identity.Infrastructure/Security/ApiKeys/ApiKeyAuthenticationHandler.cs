using System.Security.Claims;
using System.Text.Encodings.Web;
using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Infrastructure.Security.ApiKeys;

/// <summary>
/// API-key authentication handler: reads <c>Authorization: Bearer ck_…</c>,
/// resolves the row by its public prefix (one indexed lookup), verifies
/// the HMAC in constant time, refuses revoked keys and disabled owners,
/// and builds the same principal grammar the cookie scheme produces.
/// <c>last_used</c> is bumped on a throttle to avoid write amplification.
/// </summary>
/// <param name="options"></param>
/// <param name="loggerFactory"></param>
/// <param name="encoder"></param>
/// <param name="apiKeyStore"></param>
/// <param name="userStore"></param>
/// <param name="hasher"></param>
/// <param name="keyOptions"></param>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeySchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyStore apiKeyStore,
    IUserAccountStore userStore,
    ApiKeyHasher hasher,
    IOptions<ApiKeyOptions> keyOptions) : AuthenticationHandler<ApiKeySchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Digest of a dummy token — verified on the not-found path so both
    /// paths pay the HMAC cost and prefix probing gains no timing signal.
    /// Same length as a real digest, unlike the shorter alternative.
    /// </summary>
    private const string DummyDigest = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>Accepts only well-formed <c>ck_</c> bearer tokens.</summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var headerValue = headerValues.ToString();

        if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = headerValue[BearerPrefix.Length..].Trim();

        if (ApiKeyToken.Parse(rawToken) is not { } token)
        {
            return AuthenticateResult.Fail("malformed api key");
        }

        if (await apiKeyStore.FindByPrefixAsync(token.Prefix, Context.RequestAborted) is not { } apiKey)
        {
            hasher.Verify(rawToken, DummyDigest);
            return AuthenticateResult.Fail("unknown api key");
        }

        if (!apiKey.IsActive)
        {
            return AuthenticateResult.Fail("revoked api key");
        }

        if (!hasher.Verify(rawToken, apiKey.KeyHmac))
        {
            return AuthenticateResult.Fail("invalid api key");
        }

        // A disabled owner closes every one of its keys without anybody
        // revoking them one by one.
        if (await userStore.FindByIdAsync(apiKey.UserId, Context.RequestAborted) is not { } owner || owner.Disabled)
        {
            return AuthenticateResult.Fail("owner disabled or missing");
        }

        if (apiKey.LastUsedAt is null || DateTimeOffset.UtcNow - apiKey.LastUsedAt > keyOptions.Value.LastUsedRefreshInterval)
        {
            apiKey.MarkUsed(DateTimeOffset.UtcNow);
            await apiKeyStore.SaveAsync(apiKey, Context.RequestAborted);
        }

        var principal = ApiKeyPrincipals.Build(apiKey);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthSchemes.ApiKey));
    }
}

/// <summary>
/// The principal shape an authenticated API key produces — the same claim
/// grammar as the cookie scheme, plus the <c>comuki_api_key_id</c> marker
/// that makes the subject resolve to the key (not its owner).
/// </summary>
file static class ApiKeyPrincipals
{
    public static ClaimsPrincipal Build(ApiKey apiKey)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.UserId.Value.ToString()),
            new(ClaimTypes.Name, apiKey.Name),
            new(IdentityClaimNames.ApiKeyId, apiKey.Id.Value.ToString()),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.ApiKey));
    }
}
