using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace Comuki.Modules.Projects.Infrastructure.Persistence.Stores;

/// <summary>
/// DB implementation of <see cref="IProjectSettingsStore"/> — singleton,
/// because the compute adapter (a singleton in the composition root) reads
/// it and the snapshot cache must be process-wide. DB access goes through
/// <see cref="IDbContextFactory{ProjectsDbContext}"/> (each operation its own
/// short-lived context); settings entities are therefore always detached,
/// and updates are applied onto a freshly loaded tracked row. Every save
/// refreshes the shared cache and fires the project's change token — that
/// is the live-reload mechanism.
/// </summary>
/// <param name="dbFactory"></param>
/// <param name="cache"></param>
public sealed class DbProjectSettingsStore(
    IDbContextFactory<ProjectsDbContext> dbFactory,
    ProjectSettingsCache cache) : IProjectSettingsStore
{
    /// <inheritdoc />
    public async Task<ProjectSettings?> FindAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.ProjectSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);

        if (settings is { } found)
        {
            cache.Warm(found);
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<ProjectSettings> SaveAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.ProjectSettings
            .SingleOrDefaultAsync(current => current.ProjectId == settings.ProjectId, cancellationToken);

        ProjectSettings saved;
        if (row is null)
        {
            // a missing row with a version above 1 means it was deleted (or
            // was never read) — refuse instead of resurrecting it as version N
            if (settings.Version is not 1)
            {
                throw new ProjectSettingsConflictException(settings.ProjectId, settings.Version, 0);
            }

            db.ProjectSettings.Add(settings);
            saved = settings;
        }
        else
        {
            // expected-version check: the caller mutated an entity loaded at
            // row.Version, so its version must be exactly one ahead
            if (settings.Version != row.Version + 1)
            {
                throw new ProjectSettingsConflictException(settings.ProjectId, settings.Version, row.Version);
            }

            row.Apply(
                settings.MinIdle,
                settings.MaxConcurrent,
                settings.IdleTtlSeconds,
                settings.ApproveRequired,
                settings.KnowledgeEnabled,
                settings.VerifyEnabled,
                settings.ProxyEnabled,
                settings.SoftBudgetUsdMicros,
                settings.HardBudgetUsdMicros,
                settings.UpdatedAt);
            saved = row;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // the version concurrency token lost a writer race — surface the
            // typed conflict; the raw EF exception never leaves the module
            throw new ProjectSettingsConflictException(settings.ProjectId, settings.Version, row?.Version ?? 0, exception);
        }
        catch (DbUpdateException exception) when (row is null)
        {
            // insert race: the row appeared between the load and the save
            throw new ProjectSettingsConflictException(settings.ProjectId, settings.Version, 0, exception);
        }

        cache.Refresh(saved);
        return saved;
    }

    /// <inheritdoc />
    public ProjectSettings? GetCached(ProjectId projectId)
    {
        return cache.Get(projectId);
    }

    /// <inheritdoc />
    public IChangeToken GetChangeToken(ProjectId projectId)
    {
        return cache.GetChangeToken(projectId);
    }
}
