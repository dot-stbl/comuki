namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Toggles the disabled flag on an existing user account. Disabling
/// preserves grants so a returning account returns as itself; the cookie
/// scheme's stamp check kills the live session through the
/// <see cref="Domain.Users.User.BumpTokensVersion"/> path.
/// </summary>
/// <param name="UserId">Target account id.</param>
/// <param name="Disabled">New disabled flag value.</param>
public sealed record SetUserDisabledCommand(Guid UserId, bool Disabled);
