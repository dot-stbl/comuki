using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Creates a local account. The password is hashed with the BCL
/// <see cref="PasswordHasher{User}"/> (PBKDF2) before anything is
/// persisted; a duplicate email is refused loudly — the unique index
/// below backs the check.
/// </summary>
/// <param name="userStore"></param>
/// <param name="passwordHasher"></param>
/// <param name="clock"></param>
public sealed class CreateUserHandler(
    IUserAccountStore userStore,
    IPasswordHasher<User> passwordHasher,
    TimeProvider clock)
{
    /// <summary>Creates the account.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The email is already taken.</exception>
    public async Task<UserAccountView> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (await userStore.FindByEmailAsync(command.Email, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"user '{command.Email.Trim().ToLowerInvariant()}' already exists");
        }

        // boundary: the stock PasswordHasher ignores the user instance entirely
        // (per-user hashing would need a custom IPasswordHasher) — null is safe here
        var passwordHash = passwordHasher.HashPassword(null!, command.Password);
        var user = User.Create(command.Email, command.DisplayName, passwordHash, clock.GetUtcNow());

        await userStore.SaveAsync(user, cancellationToken);

        return AccountMapper.ToView(user);
    }
}
