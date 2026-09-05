using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Manually links an external OIDC identity to a local account. Refuses
/// duplicates (the unique index on <c>oidc_links.(provider, subject)</c>
/// backs the check) and unknown users (operator mistakes catch loud).
/// </summary>
/// <param name="userStore"></param>
/// <param name="oidcLinks"></param>
/// <param name="clock"></param>
public sealed class LinkOidcSubjectHandler(
    IUserAccountStore userStore,
    IOidcLinkStore oidcLinks,
    TimeProvider clock)
{
    /// <summary>Persists the link.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Unknown user, or the provider/subject is already bound.</exception>
    public async Task<OidcLinkView> HandleAsync(LinkOidcSubjectCommand command, CancellationToken cancellationToken = default)
    {
        var userId = new UserId(command.UserId);

        if (await userStore.FindByIdAsync(userId, cancellationToken) is null)
        {
            throw new InvalidOperationException($"user {userId} not found");
        }

        if (await oidcLinks.FindAsync(command.Provider, command.Subject, cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                $"oidc link '{command.Provider}/{command.Subject}' is already bound");
        }

        var link = OidcLink.Create(userId, command.Provider, command.Subject, clock.GetUtcNow());
        await oidcLinks.SaveAsync(link, cancellationToken);

        return OidcLinkView.Of(link);
    }
}
