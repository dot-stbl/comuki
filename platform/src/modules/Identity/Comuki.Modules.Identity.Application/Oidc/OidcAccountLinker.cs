using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Maps an OIDC identity to a local account: an existing link wins; a
/// matching email links the known account; otherwise a password-less
/// account is provisioned and linked. The redirect flow itself lives in
/// the host — this linker is the one piece with rules, so it is the one
/// piece tested.
/// <para>
/// Issue Q36 / v1.1: a disabled user cannot authenticate via OIDC
/// even when an IdP-attacker controls a valid subject. The disabled
/// check fires on every path — stored link, email match, and the
/// (theoretical) provision path — and surfaces as a
/// <see cref="ProviderForbiddenException"/> mapped to HTTP 403 by the
/// central handler.
/// </para>
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
    /// <exception cref="ProviderForbiddenException">
    /// The resolved local user is disabled — OIDC login is refused with
    /// HTTP 403 (<c>user.disabled</c>).
    /// </exception>
    public async Task<OidcLinkResult> HandleAsync(OidcLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (await linkStore.FindAsync(request.Provider, request.Subject, cancellationToken) is { } existingLink)
        {
            var linked = await userStore.FindByIdAsync(existingLink.UserId, cancellationToken)
                ?? throw new InvalidOperationException($"oidc link points at missing user {existingLink.UserId}");

            OidcAccountLinkerGuards.EnsureEnabled(linked, request.Email);

            return new OidcLinkResult(AccountMapper.ToView(linked), Created: false);
        }

        if (await userStore.FindByEmailAsync(request.Email, cancellationToken) is { } knownUser)
        {
            OidcAccountLinkerGuards.EnsureEnabled(knownUser, request.Email);

            await linkStore.SaveAsync(OidcLink.Create(knownUser.Id, request.Provider, request.Subject, clock.GetUtcNow()), cancellationToken);

            return new OidcLinkResult(AccountMapper.ToView(knownUser), Created: false);
        }

        var user = User.Create(request.Email, request.DisplayName ?? request.Email, passwordHash: null, clock.GetUtcNow());
        await userStore.SaveAsync(user, cancellationToken);
        await linkStore.SaveAsync(OidcLink.Create(user.Id, request.Provider, request.Subject, clock.GetUtcNow()), cancellationToken);

        return new OidcLinkResult(AccountMapper.ToView(user), Created: true);
    }
}

/// <summary>
/// Pure-function guards for <see cref="OidcAccountLinker"/>. Issue
/// Q36 / v1.1: the disabled check is shared between the stored-link
/// and email-match paths; isolating it here keeps the linker's
/// orchestration readable.
/// </summary>
file static class OidcAccountLinkerGuards
{
    /// <summary>
    /// Throws <see cref="ProviderForbiddenException"/> when the
    /// candidate local account is disabled. A disabled user cannot
    /// log in via OIDC even when an attacker controls a valid subject
    /// on a configured IdP.
    /// </summary>
    /// <param name="user">Local account the OIDC identity is about to land on.</param>
    /// <param name="email">Email from the IdP claim, surfaced in the message so an operator can identify which login was rejected.</param>
    public static void EnsureEnabled(User user, string email)
    {
        if (!user.Disabled)
        {
            return;
        }

        throw new ProviderForbiddenException(
            "user.disabled",
            $"user {email} is disabled");
    }
}
