using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Comuki.Modules.Identity.Application.Sessions;

/// <summary>
/// Verifies local credentials. Pure on purpose — issuing the cookie is
/// the infrastructure's job (<c>IUserAuthenticationService</c>), so this
/// handler stays unit-testable without HTTP plumbing.
/// </summary>
/// <param name="userStore"></param>
/// <param name="passwordHasher"></param>
public sealed class LoginHandler(
    IUserAccountStore userStore,
    IPasswordHasher<User> passwordHasher)
{
    /// <summary>Checks the credentials and returns the login outcome.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        if (await userStore.FindByEmailAsync(command.Email, cancellationToken) is not { } user)
        {
            return LoginResult.Failed(LoginResult.FailureInvalidCredentials);
        }

        if (user.Disabled)
        {
            return LoginResult.Failed(LoginResult.FailureDisabled);
        }

        if (user.PasswordHash is not { } storedHash)
        {
            return LoginResult.Failed(LoginResult.FailureNoPassword);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, storedHash, command.Password);

        return verification == PasswordVerificationResult.Success
            ? LoginResult.Succeeded(user.Id, user.TokensVersion)
            : LoginResult.Failed(LoginResult.FailureInvalidCredentials);
    }
}
