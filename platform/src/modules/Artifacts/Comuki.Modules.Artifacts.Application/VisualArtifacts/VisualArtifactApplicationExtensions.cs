using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts;

/// <summary>
/// DI extension for the visual-artifact surface. The
/// <see cref="IVisualArtifactStore"/> falls back to a no-op stub when
/// the host has not wired the EF-backed implementation — same pattern
/// as the run-artifact bundle store, so unit tests compose the module
/// in isolation without a database.
/// </summary>
public static class VisualArtifactApplicationExtensions
{
    /// <summary>
    /// Registers <see cref="VisualArtifactService"/>, the null store,
    /// and the no-op work-item source.
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddVisualArtifactsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IVisualArtifactStore, NullVisualArtifactStore>();
        services.TryAddSingleton<IWorkItemArtifactSource, NullWorkItemArtifactSource>();
        services.AddScoped<VisualArtifactService>();
        // IRunJournal is registered by the engine composition; the no-op
        // fallback for tests lives in Comuki.Shared.Contracts.
        return services;
    }
}

/// <summary>Null store: every upload is dropped, every find / download returns <c>null</c>. Tests compose the module without DB + MinIO.</summary>
internal sealed class NullVisualArtifactStore : IVisualArtifactStore
{
    public Task<Domain.VisualArtifacts.VisualArtifact> UploadVisualAsync(VisualArtifactUploadRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("NullVisualArtifactStore does not persist; the host must wire the EF-backed implementation.");
    }

    public Task<Domain.VisualArtifacts.VisualArtifact?> FindAsync(Domain.VisualArtifacts.VisualArtifactId artifactId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Domain.VisualArtifacts.VisualArtifact?>(null);
    }

    public Task<Domain.VisualArtifacts.VisualArtifact?> FindInProjectAsync(Domain.VisualArtifacts.VisualArtifactId artifactId, Shared.Kernel.Ids.ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Domain.VisualArtifacts.VisualArtifact?>(null);
    }

    public Task<VisualArtifactContent?> DownloadVisualInProjectAsync(Domain.VisualArtifacts.VisualArtifactId artifactId, Shared.Kernel.Ids.ProjectId projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<VisualArtifactContent?>(null);
    }
}

/// <summary>Null source: every ownership probe returns <c>null</c> (== 409). Tests compose without the engine DbContext.</summary>
internal sealed class NullWorkItemArtifactSource : IWorkItemArtifactSource
{
    public Task<WorkItemOwnership?> FindOwnedAsync(Guid workItemId, Shared.Kernel.Ids.WorkerId workerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<WorkItemOwnership?>(null);
    }
}
