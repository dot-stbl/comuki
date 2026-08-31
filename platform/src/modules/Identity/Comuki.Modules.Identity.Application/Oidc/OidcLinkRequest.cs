namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// An external OIDC identity to resolve into a local account — what the
/// redirect flow hands to the linker after a successful callback.
/// </summary>
/// <param name="Provider">Provider name as configured in <c>auth:oidc:providers</c>.</param>
/// <param name="Subject">The <c>sub</c> claim.</param>
/// <param name="Email">The <c>email</c> claim.</param>
/// <param name="DisplayName">Optional display name for account auto-creation.</param>
public sealed record OidcLinkRequest(string Provider, string Subject, string Email, string? DisplayName);
