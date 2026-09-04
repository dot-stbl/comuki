using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Verifies the signature, issuer, audience and lifetime of an OIDC
/// <c>id_token</c> against the discovery document's JWKS. Returns the
/// verified claims — never the raw JWT, never a parsed-but-unsigned
/// payload.
/// </summary>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcIdTokenValidator(ILogger<OidcIdTokenValidator> logger)
{
    /// <summary>The claim bundle the linker expects: sub + email + name.</summary>
    public sealed record VerifiedClaims(string Subject, string Email, string? DisplayName);

    /// <summary>
    /// Validates <paramref name="idToken"/> against the IdP's keys and
    /// returns the claims. Throws on any signature/issuer/audience/lifetime
    /// mismatch — callers surface those as <c>oidc.id_token_invalid</c>.
    /// </summary>
    /// <param name="idToken"></param>
    /// <param name="discovery"></param>
    /// <param name="expectedAudience">The client id the token was issued for.</param>
    /// <param name="cancellationToken"></param>
    public VerifiedClaims Validate(
        string idToken,
        OpenIdConnectConfiguration discovery,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = discovery.Issuer,
            ValidateAudience = true,
            ValidAudience = expectedAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = discovery.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var handler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, parameters, out var validatedToken);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Oidc id_token validation failed");
            throw new InvalidOperationException($"oidc id_token validation failed: {ex.Message}", ex);
        }

        var subject = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        var name = principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? throw new InvalidOperationException("oidc id_token carries no sub claim")
            : string.IsNullOrWhiteSpace(email)
            ? throw new InvalidOperationException("oidc id_token carries no email claim")
            : new VerifiedClaims(subject, email, name);
    }
}
