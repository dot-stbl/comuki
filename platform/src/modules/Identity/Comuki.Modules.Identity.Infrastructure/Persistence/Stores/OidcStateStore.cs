using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Oidc;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IOidcStateStore"/>: single-use reads
/// are <c>SELECT … FOR UPDATE</c>-free — a transaction that loads the
/// row, deletes it, and commits is enough because the row id is a
/// UUIDv7 the browser cannot replay, and a stale row is rejected by
/// the expiry check anyway.
/// </summary>
/// <param name="db"></param>
public sealed class OidcStateStore(IdentityDbContext db) : IOidcStateStore
{
    /// <inheritdoc />
    public async Task SaveAsync(OidcState state, CancellationToken cancellationToken = default)
    {
        if (db.Entry(state).State == EntityState.Detached)
        {
            db.OidcStates.Add(state);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OidcState?> ConsumeAsync(OidcStateId id, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var row = await db.OidcStates.SingleOrDefaultAsync(state => state.Id == id, cancellationToken);

        if (row is null)
        {
            return null;
        }

        db.OidcStates.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return row;
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await db.OidcStates
            .Where(state => state.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
