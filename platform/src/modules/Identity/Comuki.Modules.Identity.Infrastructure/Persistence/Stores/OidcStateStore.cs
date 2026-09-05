using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.Oidc;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Identity.Infrastructure.Persistence.Stores;

/// <summary>
/// EF implementation of <see cref="IOidcStateStore"/>: single-use reads
/// are <c>SELECT … FOR UPDATE</c>-free — a transaction that loads the
/// row, deletes it, and commits is enough because the row id is a
/// UUIDv7 the browser cannot replay, and a stale row is rejected by
/// the expiry check on <see cref="ConsumeAsync"/>.
/// </summary>
/// <param name="db"></param>
/// <param name="clock">Injected <see cref="TimeProvider" /> for the <c>ExpiresAt</c> gate.</param>
public sealed class OidcStateStore(IdentityDbContext db, TimeProvider clock) : IOidcStateStore
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

        // Contract: "A stale row (past ExpiresAt) reads as null even if the row
        // exists — the same single-use guarantee either way." Without this gate
        // a row that the sweep hasn't reached yet would still hand out its
        // verifier to a caller whose `state` is past its TTL.
        if (row.ExpiresAt <= clock.GetUtcNow())
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
