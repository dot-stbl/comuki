namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Invites a local user account. <paramref name="Password"/> is optional —
/// null means the operator intends to send a bootstrap invitation link
/// separately (the new account lands password-less for now).
/// </summary>
/// <param name="Email"></param>
/// <param name="DisplayName"></param>
/// <param name="Password">Optional bootstrap password (min 8 chars when present).</param>
public sealed record InviteUserCommand(string Email, string? DisplayName, string? Password);
