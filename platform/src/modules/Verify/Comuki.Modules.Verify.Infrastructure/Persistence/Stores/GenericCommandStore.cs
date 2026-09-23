using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Verify.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IGenericCommandStore"/> over the <see cref="VerifyDbContext"/>.
/// The pending-runs query runs with <c>FOR UPDATE SKIP LOCKED</c> so two
/// host replicas never claim the same row.
/// </summary>
/// <param name="db">Verify context of the current scope.</param>
public sealed class GenericCommandStore(VerifyDbContext db) : IGenericCommandStore
{
    /// <inheritdoc />
    public async Task AddAsync(GenericCommandRun run, CancellationToken cancellationToken = default)
    {
        db.GenericCommandRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<GenericCommandRun?> FindAsync(GenericCommandRunId runId, CancellationToken cancellationToken = default)
    {
        return db.GenericCommandRuns
            .FirstOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GenericCommandRun>> ListAsync(
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await db.GenericCommandRuns
            .AsNoTracking()
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(GenericCommandRun run, CancellationToken cancellationToken = default)
    {
        db.GenericCommandRuns.Update(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GenericCommandRun>> ClaimPendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // $$"""..."""  — a double-$ raw string needs {{expr}} to interpolate,
        // so the single-brace {0} stays literal text for FromSqlRaw's own
        // (unrelated) positional-parameter placeholder.
        return await db.GenericCommandRuns
            .FromSqlRaw(
                $$"""
                SELECT * FROM {{VerifyDatabase.Schema}}.{{VerifyDatabase.GenericCommandRuns}}
                WHERE status = 'Pending'
                ORDER BY created_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED
                """,
                limit)
            .ToListAsync(cancellationToken);
    }
}
