using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IUserAccountStore"/> over
/// <see cref="IdentityDbContext"/>. Scoped — one context per unit of work.
/// </summary>
/// <param name="db"></param>
public sealed class UserAccountStore(IdentityDbContext db) : IUserAccountStore
{
    /// <inheritdoc />
    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        return await db.Users.SingleOrDefaultAsync(user => user.Email == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> FindByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await db.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(User user, CancellationToken cancellationToken = default)
    {
        if (db.Entry(user).State == EntityState.Detached)
        {
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        string? emailContains,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(emailContains))
        {
            var needle = emailContains.Trim().ToLowerInvariant();
            query = query.Where(user => user.Email.Contains(needle));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(user => user.Email)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
