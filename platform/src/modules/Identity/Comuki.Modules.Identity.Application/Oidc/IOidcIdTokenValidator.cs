using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Port over id_token signature verification: production impl is
/// <see cref="OidcIdTokenValidator"/> (JWKS-backed JWT validation);
/// the port lets unit tests substitute a fake and skip the real
/// cryptographic verification.
/// </summary>
public interface IOidcIdTokenValidator
{
    /// <summary>Validates the id_token signature + issuer + audience + lifetime against the discovery doc's JWKS.</summary>
    /// <param name="idToken"></param>
    /// <param name="discovery"></param>
    /// <param name="expectedAudience"></param>
    /// <param name="cancellationToken"></param>
    public OidcVerifiedClaims Validate(
        string idToken,
        OpenIdConnectConfiguration discovery,
        string expectedAudience,
        CancellationToken cancellationToken = default);
}

/// <summary>The claim bundle the linker expects: sub + email + name + email_verified.</summary>
/// <param name="Subject">The <c>sub</c> claim — the IdP-side stable identifier.</param>
/// <param name="Email">The <c>email</c> claim.</param>
/// <param name="DisplayName">Optional display name.</param>
/// <param name="EmailVerified">True only when the IdP vouches the email. The validator and the linker both gate on this (security audit A01-2): an unverified email is never enough to bind a Comuki account.</param>
public sealed record OidcVerifiedClaims(string Subject, string Email, string? DisplayName, bool EmailVerified);
