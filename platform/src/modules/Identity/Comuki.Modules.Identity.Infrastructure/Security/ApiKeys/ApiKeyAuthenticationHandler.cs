using System.Security.Claims;
using System.Text.Encodings.Web;
using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Infrastructure.Security.ApiKeys;

/// <summary>
/// API-key authentication handler: reads <c>Authorization: Bearer ck_…</c>,
/// resolves the row by its public prefix (one indexed lookup), verifies
/// the HMAC constant-time, refuses revoked keys and disabled owners,
/// and — when the key carries a <see cref="ApiKey.TenantProjectId"/>
/// — enforces the matching <c>X-Comuki-Tenant</c> header. The handler
/// fails authentication when the tenant header is missing or wrong;
/// <see cref="HandleChallengeAsync"/> flips the response to 403 in that
/// case (the framework defaults to 401 for AuthenticateResult.Fail).
/// <c>last_used</c> is bumped on a throttle to avoid write amplification.
/// </summary>
/// <param name="options"></param>
/// <param name="loggerFactory"></param>
/// <param name="encoder"></param>
/// <param name="scopeAccessor"></param>
/// <param name="apiKeyStore"></param>
/// <param name="userStore"></param>
/// <param name="hasher"></param>
/// <param name="keyOptions"></param>
/// <param name="clock">
///     Injected <see cref="TimeProvider" /> for the <c>last_used</c> throttle
///     check — the base <c>AuthenticationHandler</c> doesn't expose one, so the
///     handler resolves <c>now</c> via this additional ctor parameter.
/// </param>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeySchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISubjectScopeAccessor scopeAccessor,
    IApiKeyStore apiKeyStore,
    IUserAccountStore userStore,
    ApiKeyHasher hasher,
    IOptions<ApiKeyOptions> keyOptions,
    TimeProvider clock) : AuthenticationHandler<ApiKeySchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Header that carries the tenant id on scoped keys.</summary>
    internal const string TenantHeader = "X-Comuki-Tenant";

    /// <summary>Set on the request when a scoped key authenticates against a mismatching tenant.</summary>
    internal const string TenantMismatchFlag = "comuki.api_key.tenant_mismatch";

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

        // Authentication runs before `SubjectScopeMiddleware`, so there is no
        // subject yet — by definition, since deciding who the caller is what
        // this handler is for. Both stores it reads are query-filtered, so
        // without a system scope the key lookup matches nothing and every
        // valid key is rejected as unknown. Held to the end of the method:
        // the owner read and the last-used write are the same flow.
        using var systemScope = scopeAccessor.AsSystem("api-key-auth");

        if (await apiKeyStore.FindByPrefixAsync(token.Prefix, Context.RequestAborted) is not { } apiKey)
        {
            // Constant-time dummy verify: defeat timing oracles that would
            // otherwise reveal whether a prefix maps to a real key.
            // Result discarded by design — only the work itself matters.
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

        // Tenant scope: a non-null TenantProjectId binds the key to one
        // project. The header MUST be present and parse to the same
        // project id; both missing and wrong fail authentication (403 via
        // HandleChallengeAsync below).
        if (apiKey.TenantProjectId is { } boundTenant
            && !HasMatchingTenant(boundTenant))
        {
            Context.Items[TenantMismatchFlag] = true;
            return AuthenticateResult.Fail("api key tenant scope mismatch");
        }

        // A disabled owner closes every one of its keys without anybody
        // revoking them one by one.
        if (await userStore.FindByIdAsync(apiKey.UserId, Context.RequestAborted) is not { } owner || owner.Disabled)
        {
            return AuthenticateResult.Fail("owner disabled or missing");
        }

        var now = clock.GetUtcNow();
        if (apiKey.LastUsedAt is null || now - apiKey.LastUsedAt > keyOptions.Value.LastUsedRefreshInterval)
        {
            apiKey.MarkUsed(now);
            await apiKeyStore.SaveAsync(apiKey, Context.RequestAborted);
        }

        var principal = ApiKeyPrincipals.Build(apiKey);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, AuthSchemes.ApiKey));
    }

    /// <summary>
    /// Challenge response: 401 for the standard "missing / unknown /
    /// revoked" failures; 403 when <see cref="HandleAuthenticateAsync"/>
    /// set <see cref="TenantMismatchFlag"/> — the bearer authenticated,
    /// but not against the requested tenant.
    /// </summary>
    /// <param name="properties"></param>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items[TenantMismatchFlag] is true)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Response.Headers.WWWAuthenticate = $"Bearer realm=\"comuki\", error=\"insufficient_scope\", error_description=\"tenant scope mismatch\"";
        }
        else
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer realm=\"comuki\"";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True when the request's <see cref="TenantHeader"/> parses as the
    /// same guid the key was bound to. Absent / unparsable / different
    /// id all fail.
    /// </summary>
    /// <param name="bound">The tenant id the key was issued under.</param>
    private bool HasMatchingTenant(ProjectId bound)
    {
        return Request.Headers.TryGetValue(TenantHeader, out var headerValues)
            && Guid.TryParse(headerValues.ToString().Trim(), out var parsed)
            && string.Equals(
                parsed.ToString("D"),
                bound.Value.ToString("D"),
                StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The principal shape an authenticated API key produces — the same claim
/// grammar as the cookie scheme, plus the <c>comuki_api_key_id</c> marker
/// that makes the subject resolve to the key (not its owner), and the
/// optional <c>comuki_api_key_tenant</c> claim carrying the bound tenant id.
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

        if (apiKey.TenantProjectId is { } tenant)
        {
            claims.Add(new Claim(IdentityClaimNames.ApiKeyTenant, tenant.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.ApiKey));
    }
}
