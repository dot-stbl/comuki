using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Invites a local account. The password is optional — when present it is
/// hashed with the BCL <see cref="PasswordHasher{User}"/> (PBKDF2) before
/// persistence; when absent the account lands password-less and waits for
/// the operator to send a bootstrap link. Duplicate emails are refused
/// loudly — the unique index on <c>users.email</c> backs the check. An
/// email that already maps to an OIDC-linked user is refused too
/// (Q43 — refuse invite when email OIDC-linked), so an admin cannot
/// quietly create a local shadow of an identity already federated to
/// another IdP.
/// </summary>
/// <param name="userStore"></param>
/// <param name="linkStore"></param>
/// <param name="passwordHasher"></param>
/// <param name="clock"></param>
public sealed class InviteUserHandler(
    IUserAccountStore userStore,
    IOidcLinkStore linkStore,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock)
{
    /// <summary>Creates the invited account.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <exception cref="InvalidOperationException">The email is already taken.</exception>
    /// <exception cref="OidcLinkConflictException">The email is bound to an existing OIDC-linked user.</exception>
    public async Task<UserAccountView> HandleAsync(InviteUserCommand command, CancellationToken cancellationToken = default)
    {
        var normalized = command.Email.Trim().ToLowerInvariant();

        if (await userStore.FindByEmailAsync(command.Email, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"user '{normalized}' already exists");
        }

        if (await linkStore.FindByEmailAsync(command.Email, cancellationToken) is not null)
        {
            throw new OidcLinkConflictException(normalized);
        }

        // boundary: the stock PasswordHasher ignores the user instance entirely
        // (per-user hashing would need a custom IPasswordHasher) — null is safe here
        var passwordHash = command.Password is { Length: > 0 }
            ? passwordHasher.HashPassword(null!, command.Password)
            : null;

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? command.Email.Split('@')[0]
            : command.DisplayName;
        var user = User.Create(command.Email, displayName, passwordHash, clock.GetUtcNow());

        await userStore.SaveAsync(user, cancellationToken);

        return AccountMapper.ToView(user);
    }
}

/// <summary>
/// Raised when an invite email already maps to a user that has any OIDC
/// link (Q43). Carries the offending email so the 409 row can surface a
/// precise message without re-reading the user store; the central
/// problem-handler registry owns the wire mapping via
/// <c>OidcLinkConflictProblemHandler</c>.
/// </summary>
/// <param name="Email">Lower-cased email the caller attempted to invite.</param>
public sealed class OidcLinkConflictException(string Email)
    : DomainException(ErrorCode, $"user with email '{Email}' is already linked to an OIDC identity; invite refused")
{
    private const string ErrorCode = "user.oidc_link_exists";

    /// <summary>Lower-cased email the caller attempted to invite.</summary>
    public string Email { get; } = Email;
}
