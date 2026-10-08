using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Comuki.Modules.Integrations.Infrastructure.Persistence.Stores;

/// <summary>
/// EF tracker and SaveChanges helpers used by the stores where the
/// orchestrator does the I/O and these handle the
/// duplicate-key-detach / unique-violation-tolerance pattern at a single
/// seam. Extracted per the no-private-methods rule.
/// </summary>
internal static class EfDbContextExtensions
{
    /// <summary>
    /// Detaches the currently-tracked entry of <typeparamref name="T"/>
    /// whose entity matches <paramref name="entitySelector"/>; no-op
    /// when nothing is tracked for that entity. Required before the
    /// detached-update path to avoid the EF duplicate-key attach.
    /// </summary>
    /// <typeparam name="T">Entity type currently in the tracker.</typeparam>
    /// <param name="tracker">The change tracker; usually <c>db.ChangeTracker</c>.</param>
    /// <param name="entitySelector">Predicate selecting the entity to detach.</param>
    public static void DetachTrackedBy<T>(this ChangeTracker tracker, Func<T, bool> entitySelector)
        where T : class
    {
        var tracked = tracker.Entries<T>().FirstOrDefault(entry => entitySelector(entry.Entity));
        tracked?.State = EntityState.Detached;
    }

    /// <summary>
    /// Saves pending changes and returns <c>true</c>; on
    /// <c>SQLSTATE 23505</c> (unique_violation) the offending entity is
    /// detached and the call returns <c>false</c>. The index is the
    /// arbiter — replay/duplicate writes are safe by construction.
    /// </summary>
    /// <param name="db">The store's <see cref="DbContext"/>.</param>
    /// <param name="entity">
    /// The freshly-added entity whose <c>SaveChanges</c> may collide;
    /// detached on the unique-violation path so the scope stays clean.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<bool> TrySaveUniqueAsync(this DbContext db, object entity, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }
}
