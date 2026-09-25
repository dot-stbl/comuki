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
/// </summary>
/// <param name="projects">The module's project-listing port.</param>
public sealed class ProjectCountLimitUsageProvider(IProjectStore projects) : ILimitUsageProvider
{
    /// <inheritdoc />
    public LimitKey LimitKey { get; } = LimitKey.Parse("projects");

    /// <inheritdoc />
    public async Task<int> CurrentAsync(CancellationToken cancellationToken)
    {
        var active = await projects.ListAsync(includeArchived: false, cancellationToken);

        return active.Count;
    }
}
