using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;

namespace Comuki.Modules.Identity.Application.Users;

/// <summary>
/// Reads a page of user accounts for the identity-admin list
/// (<c>GET /api/v1/users</c>, issue #45 / F13). The store returns the
/// already-sorted page (alphabetical by email) plus the unfiltered total;
/// the handler is a thin mapper from <see cref="Domain.Users.User"/>
/// to <see cref="UserAccountView"/>.
/// </summary>
/// <param name="userStore">Persistence port (scoped — DbContext per request).</param>
public sealed class ListUsersHandler(IUserAccountStore userStore)
{
    /// <summary>Reads the requested page.</summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    public async Task<(IReadOnlyList<UserAccountView> Items, int Total)> HandleAsync(
        ListUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        var skip = (query.Page - 1) * query.PageSize;
        var (users, total) = await userStore.ListAsync(
            query.EmailContains,
            skip,
            query.PageSize,
            cancellationToken);

        var items = new UserAccountView[users.Count];
        for (var index = 0; index < users.Count; index++)
        {
            items[index] = AccountMapper.ToView(users[index]);
        }

        return (items, total);
    }
}
