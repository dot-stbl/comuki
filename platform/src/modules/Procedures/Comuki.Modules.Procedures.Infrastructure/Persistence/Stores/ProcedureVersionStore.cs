using Comuki.Modules.Procedures.Application.Compiler.Model;
using Comuki.Modules.Procedures.Application.ProcedureVersions;
using Comuki.Modules.Procedures.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Procedures.Infrastructure.Persistence.Stores;

/// <summary>
/// EF-backed immutable version store. Write-once: the first insert with
/// a given content-addressed <c>version_id</c> wins; subsequent inserts
/// with the same id are byte-identical no-ops (content deduplication).
/// Reads are byte-identical to the stored record. Singleton: the store
/// owns no per-request state and reaches the database through the
/// context factory (a fresh context per operation), so singleton
/// consumers — the publication and diff services — do not capture a
/// scoped <see cref="ProceduresDbContext"/>.
/// </summary>
public sealed class ProcedureVersionStore(
    IDbContextFactory<ProceduresDbContext> contextFactory,
    TimeProvider clock) : IProcedureVersionStore
{
    /// <inheritdoc />
    public async Task SaveAsync(
        CompiledProcedureVersion version,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.CompiledProcedureVersions
            .FirstOrDefaultAsync(
                entity => entity.VersionId == version.VersionId,
                cancellationToken);
        if (existing is not null)
        {
            return;
        }

        dbContext.CompiledProcedureVersions.Add(new CompiledProcedureVersionEntity
        {
            VersionId = version.VersionId,
            ProjectId = version.ProjectId,
            ProcedureKey = version.ProcedureKey,
            CatalogVersion = version.CatalogVersion,
            SourceRef = version.SourceRef,
            GraphJson = version.GraphJson,
            CreatedAt = clock.GetUtcNow(),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CompiledProcedureVersion?> GetAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.CompiledProcedureVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.VersionId == versionId,
                cancellationToken);

        return entity is null
            ? null
            : new CompiledProcedureVersion(
                entity.VersionId,
                entity.ProjectId,
                entity.ProcedureKey,
                entity.CatalogVersion,
                entity.SourceRef,
                entity.GraphJson);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompiledProcedureVersion>> ListByProcedureAsync(
        Guid projectId,
        string procedureKey,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await dbContext.CompiledProcedureVersions
            .AsNoTracking()
            .Where(entity => entity.ProjectId == projectId && entity.ProcedureKey == procedureKey)
            .OrderByDescending(static entity => entity.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. entities
            .Select(static entity => new CompiledProcedureVersion(
                entity.VersionId,
                entity.ProjectId,
                entity.ProcedureKey,
                entity.CatalogVersion,
                entity.SourceRef,
                entity.GraphJson))];
    }
}
