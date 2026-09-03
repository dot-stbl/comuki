using Comuki.Modules.Artifacts.Application.Packaging;
using Comuki.Modules.Artifacts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of <see cref="IRunArtifactBundleStore"/> over
/// the artifacts schema. The packager calls this on every poll; reads are
/// no-tracking, writes are scoped to the request's DbContext.
/// </summary>
/// <param name="db">Scoped artifacts DbContext.</param>
public sealed class EfRunArtifactBundleStore(ArtifactsDbContext db) : IRunArtifactBundleStore
{
    /// <inheritdoc />
    public async Task<bool> IsBundledAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await db.RunBundles
            .AsNoTracking()
            .AnyAsync(bundle => bundle.RunId == runId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordAsync(RunArtifactBundle bundle, CancellationToken cancellationToken = default)
    {
        // Upsert via the primary key — a re-packaged run (e.g. after a
        // transient MinIO outage that was retried by the operator) replaces
        // the existing row instead of failing on the duplicate key.
        var existing = await db.RunBundles
            .FirstOrDefaultAsync(row => row.RunId == bundle.RunId, cancellationToken);

        if (existing is null)
        {
            db.RunBundles.Add(bundle);
        }
        else
        {
            existing.ProjectId = bundle.ProjectId;
            existing.Status = bundle.Status;
            existing.UploadedAt = bundle.UploadedAt;
            existing.ObjectCount = bundle.ObjectCount;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
