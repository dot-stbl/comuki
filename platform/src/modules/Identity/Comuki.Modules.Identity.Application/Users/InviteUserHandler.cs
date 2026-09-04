using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Invites a local account. The password is optional — when present it is
/// hashed with the BCL <see cref="PasswordHasher{User}"/> (PBKDF2) before
/// persistence; when absent the account lands password-less and waits for
/// the operator to send a bootstrap link. Duplicate emails are refused
/// loudly — the unique index on <c>users.email</c> backs the check.
/// </summary>
/// <param name="userStore"></param>
/// <param name="passwordHasher"></param>
/// <param name="clock"></param>
public sealed class InviteUserHandler(
    IUserAccountStore userStore,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock)
{
    /// <summary>Creates the invited account.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The email is already taken.</exception>
    public async Task<UserAccountView> HandleAsync(InviteUserCommand command, CancellationToken cancellationToken = default)
    {
        if (await userStore.FindByEmailAsync(command.Email, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"user '{command.Email.Trim().ToLowerInvariant()}' already exists");
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
