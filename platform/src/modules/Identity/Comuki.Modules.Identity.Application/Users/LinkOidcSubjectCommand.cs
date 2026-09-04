namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Manually links an external OIDC identity (provider + subject) to an
/// existing local account.
/// </summary>
/// <param name="UserId">Target local user.</param>
/// <param name="Provider">Configured provider name (kebab-case key).</param>
/// <param name="Subject">The <c>sub</c> claim the IdP emits.</param>
public sealed record LinkOidcSubjectCommand(Guid UserId, string Provider, string Subject);
