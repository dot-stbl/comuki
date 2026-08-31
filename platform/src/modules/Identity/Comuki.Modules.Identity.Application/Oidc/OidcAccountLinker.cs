using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Maps an OIDC identity to a local account: an existing link wins; a
/// matching email links the known account; otherwise a password-less
/// account is provisioned and linked. The redirect flow itself lives in
/// the host — this linker is the one piece with rules, so it is the one
/// piece tested.
/// </summary>
/// <param name="userStore"></param>
/// <param name="linkStore"></param>
/// <param name="clock"></param>
public sealed class OidcAccountLinker(
    IUserAccountStore userStore,
    IOidcLinkStore linkStore,
    TimeProvider clock)
{
    /// <summary>Resolves or provisions the local account for the external identity.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">A stored link points at a missing account.</exception>
    public async Task<OidcLinkResult> HandleAsync(OidcLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (await linkStore.FindAsync(request.Provider, request.Subject, cancellationToken) is { } existingLink)
        {
            var linked = await userStore.FindByIdAsync(existingLink.UserId, cancellationToken)
                ?? throw new InvalidOperationException($"oidc link points at missing user {existingLink.UserId}");

            return new OidcLinkResult(AccountMapper.ToView(linked), Created: false);
        }

        if (await userStore.FindByEmailAsync(request.Email, cancellationToken) is { } knownUser)
        {
            await linkStore.SaveAsync(OidcLink.Create(knownUser.Id, request.Provider, request.Subject, clock.GetUtcNow()), cancellationToken);

            return new OidcLinkResult(AccountMapper.ToView(knownUser), Created: false);
        }

        var user = User.Create(request.Email, request.DisplayName ?? request.Email, passwordHash: null, clock.GetUtcNow());
        await userStore.SaveAsync(user, cancellationToken);
        await linkStore.SaveAsync(OidcLink.Create(user.Id, request.Provider, request.Subject, clock.GetUtcNow()), cancellationToken);

        return new OidcLinkResult(AccountMapper.ToView(user), Created: true);
    }
}
