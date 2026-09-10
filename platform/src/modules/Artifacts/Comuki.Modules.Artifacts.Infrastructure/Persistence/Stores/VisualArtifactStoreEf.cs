using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Artifacts.Infrastructure.Persistence.Stores;

/// <summary>
/// EF-backed implementation of <see cref="IVisualArtifactStore"/>.
/// The metadata row lives in the artifacts schema; the bytes live in
/// MinIO under <c>visual_artifacts/{projectId}/{artifactId}/{version}</c>
/// (see <see cref="MinioVisualArtifactStorage"/>). Versioning is
/// monotonic per id: when the publisher supplies an explicit id the
/// store upserts the row + bumps the version + overwrites the bytes;
/// otherwise it mints a fresh UUIDv7.
/// </summary>
/// <param name="db">Scoped artifacts DbContext (scope-filter applies).</param>
/// <param name="storage">Singleton MinIO helper.</param>
public sealed class VisualArtifactStoreEf(
    ArtifactsDbContext db,
    MinioVisualArtifactStorage storage) : IVisualArtifactStore
{
    /// <inheritdoc />
    public async Task<VisualArtifact> UploadVisualAsync(
        VisualArtifactUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = request.ExplicitId ?? VisualArtifactId.New();
        var version = 1;

        if (request.ExplicitId is { } explicitId)
        {
            var existing = await db.VisualArtifacts
                .FirstOrDefaultAsync(artifact => artifact.Id == explicitId.Value, cancellationToken)
                ;
            version = existing is { } row ? row.Version + 1 : 1;
        }

        var artifact = new VisualArtifact
        {
            Id = id.Value,
            ProjectId = request.ProjectId.Value,
            Version = version,
            Filename = request.Filename,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            Title = request.Title,
            CreatedAt = request.CreatedAt,
            CreatedBy = request.CreatedBy,
            RunId = request.RunId,
            WorkItemId = request.WorkItemId,
            SessionId = request.SessionId,
            TicketId = request.TicketId,
        };

        await storage.UploadAsync(
            request.ProjectId,
            id,
            version,
            request.ContentType,
            request.SizeBytes,
            request.Body,
            cancellationToken);

        if (version == 1)
        {
            db.VisualArtifacts.Add(artifact);
        }
        else
        {
            db.VisualArtifacts.Update(artifact);
        }

        await db.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    /// <inheritdoc />
    public Task<VisualArtifact?> FindAsync(
        VisualArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        return db.VisualArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(artifact => artifact.Id == artifactId.Value, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VisualArtifact?> FindInProjectAsync(
        VisualArtifactId artifactId,
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        return db.VisualArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                artifact => artifact.Id == artifactId.Value && artifact.ProjectId == projectId.Value,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VisualArtifactContent?> DownloadVisualInProjectAsync(
        VisualArtifactId artifactId,
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.VisualArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                artifact => artifact.Id == artifactId.Value && artifact.ProjectId == projectId.Value,
                cancellationToken)
            ;
        if (row is null)
        {
            return null;
        }

        var stream = await storage
            .OpenReadAsync(projectId, artifactId, row.Version, cancellationToken)
            ;
        return stream is not { } body
            ? null
            : new VisualArtifactContent(body.Body, body.ContentType, body.SizeBytes);
    }
}
