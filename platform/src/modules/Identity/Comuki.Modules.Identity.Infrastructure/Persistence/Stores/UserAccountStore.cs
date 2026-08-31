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
            _ = db.Users.Add(user);
        }

        _ = await db.SaveChangesAsync(cancellationToken);
    }
}
