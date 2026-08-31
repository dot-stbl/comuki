using Comuki.Modules.Identity.Application.Ports;

namespace Comuki.Modules.Identity.Application.ApiKeys.Issue;

/// <summary>
/// Issues an API key for an account after checking it exists and is
/// enabled. The plaintext is returned exactly once via
/// <see cref="IssuedApiKeyCredential"/>.
/// </summary>
/// <param name="userStore"></param>
/// <param name="issuer"></param>
public sealed class IssueApiKeyHandler(
    IUserAccountStore userStore,
    ApiKeyIssuer issuer)
{
    /// <summary>Issues the key.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The user is unknown or disabled.</exception>
    public async Task<IssuedApiKeyCredential> HandleAsync(IssueApiKeyCommand command, CancellationToken cancellationToken = default)
    {
        var user = await userStore.FindByIdAsync(command.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"user {command.UserId} not found");

        return user.Disabled
            ? throw new InvalidOperationException($"user {command.UserId} is disabled")
            : await issuer.IssueAsync(command.UserId, command.Name, cancellationToken);
    }
}
