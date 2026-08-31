using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;

namespace Comuki.Modules.Identity.Application.Ports;

/// <summary>
/// Persistence port for user accounts. Implemented by the Identity
/// infrastructure over its DbContext; Application code never touches EF.
/// </summary>
public interface IUserAccountStore
{
    /// <summary>Finds an account by (lower-cased) email.</summary>
    /// <param name="email"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Finds an account by id.</summary>
    /// <param name="userId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<User?> FindByIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed account.</summary>
    /// <param name="user"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(User user, CancellationToken cancellationToken = default);
}
