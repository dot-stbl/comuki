using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Flips the disabled flag on an existing account. Disabling bumps the
/// tokens version (every cookie dies) but leaves grants alone — a
/// re-enabled account returns with its grants intact. Re-applying the
/// same state is a no-op (idempotent at the domain level via
/// <see cref="User.Disable"/> / <see cref="User.Enable"/>).
/// </summary>
/// <param name="userStore"></param>
/// <param name="clock"></param>
public sealed class SetUserDisabledHandler(IUserAccountStore userStore, TimeProvider clock)
{
    /// <summary>Updates the disabled flag.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Unknown user id.</exception>
    public async Task<UserAccountView> HandleAsync(SetUserDisabledCommand command, CancellationToken cancellationToken = default)
    {
        var userId = new UserId(command.UserId);
        var user = await userStore.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"user {userId} not found");

        var now = clock.GetUtcNow();

        if (command.Disabled && !user.Disabled)
        {
            user.Disable(now);
        }
        else if (!command.Disabled && user.Disabled)
        {
            user.Enable(now);
        }

        await userStore.SaveAsync(user, cancellationToken);

        return AccountMapper.ToView(user);
    }
}
