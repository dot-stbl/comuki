using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Comuki.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Default <see cref="IOidcIdTokenValidator"/>: <see cref="JwtSecurityTokenHandler"/>
/// + <see cref="TokenValidationParameters"/> (issuer, audience, lifetime,
/// JWKS signing keys from the discovery doc). Returns the verified
/// claims — never the raw JWT, never a parsed-but-unsigned payload.
/// <para>
/// Security audit A01-2: the <c>email_verified</c> claim must be
/// <c>true</c> for any downstream account link or provision — an IdP
/// that returns an unverified email never authorises a Comuki
/// account. The check fires here (early) and again at the linker
/// (defense-in-depth) — see <see cref="OidcAccountLinker"/>.
/// </para>
/// </summary>
/// <param name="logger">Diagnostic log.</param>
public sealed class OidcIdTokenValidator(ILogger<OidcIdTokenValidator> logger) : IOidcIdTokenValidator
{
    /// <summary>Stable code surfaced as the ProblemDetails <c>code</c> when the IdP did not verify the email.</summary>
    public const string EmailUnverifiedCode = "user.email_unverified";

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
        catch (SecurityTokenException exception)
        {
            logger.LogWarning(exception, "Oidc id_token validation failed");
            throw new InvalidOperationException($"oidc id_token validation failed: {exception.Message}", exception);
        }

        var subject = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        var name = principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        // email_verified: a JSON boolean serialised to a string claim
        // value ("true" / "false"). Anything other than case-insensitive
        // "true" is treated as unverified — including the absent claim,
        // the literal "false", and any other spelling an IdP might
        // produce. The host refuses to bind a Comuki account to an
        // email the IdP did not personally verify.
        var emailVerifiedRaw = principal.FindFirst("email_verified")?.Value;
        var emailVerified = string.Equals(emailVerifiedRaw, "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("oidc id_token carries no sub claim");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("oidc id_token carries no email claim");
        }

        if (!emailVerified)
        {
            logger.LogWarning(
                "Oidc id_token for subject {Subject} rejected: email_verified claim is not true",
                subject);
            throw new ProviderException(
                EmailUnverifiedCode,
                "IdP did not verify email");
        }

        return new OidcVerifiedClaims(subject, email, name, emailVerified);
    }
}
