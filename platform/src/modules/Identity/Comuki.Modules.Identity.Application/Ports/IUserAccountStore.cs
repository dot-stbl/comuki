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

    /// <summary>Lists a page of accounts (paged, filterable by email substring).</summary>
    /// <param name="emailContains">Optional case-insensitive substring filter on <c>email</c>; <c>null</c>/empty for no filter.</param>
    /// <param name="skip">Number of rows to skip (<c>(page - 1) * pageSize</c>).</param>
    /// <param name="take">Page size (1..100).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The page slice and the total row count.</returns>
    public Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        string? emailContains,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed account.</summary>
    /// <param name="user"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(User user, CancellationToken cancellationToken = default);
}
