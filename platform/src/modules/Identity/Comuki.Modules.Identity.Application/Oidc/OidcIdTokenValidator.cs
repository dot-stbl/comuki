using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Default <see cref="IOidcIdTokenValidator"/>: <see cref="JwtSecurityTokenHandler"/>
/// + <see cref="TokenValidationParameters"/> (issuer, audience, lifetime,
/// JWKS signing keys from the discovery doc). Returns the verified
/// claims — never the raw JWT, never a parsed-but-unsigned payload.
/// </summary>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcIdTokenValidator(ILogger<OidcIdTokenValidator> logger) : IOidcIdTokenValidator
{
    /// <inheritdoc />
    public OidcVerifiedClaims Validate(
        string idToken,
        OpenIdConnectConfiguration discovery,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        // JwtSecurityTokenHandler.ValidateToken is synchronous; the
        // cancellation token is part of the IOidcIdTokenValidator surface so
        // a future async validator (or a JsonWebTokenHandler swap) can honor
        // shutdown without a signature change.
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
                : new OidcVerifiedClaims(subject, email, name);
    }
}
