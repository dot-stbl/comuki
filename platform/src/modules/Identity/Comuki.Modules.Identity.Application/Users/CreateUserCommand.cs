namespace Comuki.Modules.Identity.Application.Users;

/// <summary>Creates a local user account with a password.</summary>
/// <param name="Email"></param>
/// <param name="DisplayName"></param>
/// <param name="Password"></param>
public sealed record CreateUserCommand(string Email, string DisplayName, string Password);
