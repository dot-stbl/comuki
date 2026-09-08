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

    /// <inheritdoc />
    public async Task<bool> TableExistsAsync(CancellationToken cancellationToken = default)
    {
        // Postgres <c>information_schema.tables</c> is the schema-of-record
        // for "does this table exist" — EF's model snapshot can lie
        // (the model is loaded even when the table is not) and a raw
        // <c>SELECT 1 FROM identity.oidc_states</c> would throw on a
        // missing table, which is exactly the failure the host
        // sweeper wants to surface without taking the process down.
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = @name
            );
            """;

        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = "schema";
            schemaParameter.Value = IdentityDatabase.Schema;
            command.Parameters.Add(schemaParameter);

            var nameParameter = command.CreateParameter();
            nameParameter.ParameterName = "name";
            nameParameter.Value = IdentityDatabase.OidcStates;
            command.Parameters.Add(nameParameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool exists && exists;
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }
}
