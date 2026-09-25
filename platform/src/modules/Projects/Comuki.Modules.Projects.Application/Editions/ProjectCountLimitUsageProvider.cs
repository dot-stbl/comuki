using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Gating;

namespace Comuki.Modules.Projects.Application.Editions;

/// <summary>
/// Reports the current project count for <c>Comuki.Shared.Editions.Limits.Projects</c>
/// (issue #164 / add-editions-and-licensing, chunk C's worked example
/// for the count-quota gate). "Current usage" is every non-archived
/// project platform-wide — this deployment has no separate "workspace"
/// concept yet, so one Comuki host instance IS the workspace the limit
/// caps (see issue #164 design.md's wire-shape comment "Number of
/// projects a workspace may create"; a future multi-workspace change
/// would narrow this to a workspace-scoped count).
/// <para>
/// The count comes from <see cref="IProjectStore.CountAsync"/> — a
/// scalar SQL <c>SELECT count(*)</c>, not a materialised list. This is
/// the request-time filter fast-fail and is <strong>advisory only</strong>
/// under concurrent writers; the authoritative check is the
/// transactional one in <see cref="IProjectStore.TryInsertWithProjectLimitAsync"/>,
/// which serialises competing writers via a <c>pg_advisory_xact_lock</c>
/// on the same connection as the insert.
/// </para>
/// </summary>
/// <param name="projects">The module's project-store port.</param>
public sealed class ProjectCountLimitUsageProvider(IProjectStore projects) : ILimitUsageProvider
{
    /// <inheritdoc />
    public LimitKey LimitKey { get; } = LimitKey.Parse("projects");

    /// <inheritdoc />
    public Task<int> CurrentAsync(CancellationToken cancellationToken)
    {
        return projects.CountAsync(includeArchived: false, cancellationToken);
    }
}
