using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Ports;
using Comuki.Engine.Compute.Settings;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Projects;

/// <summary>
/// Layering-safe bridge (issue #12 T4.8): Engine.Compute owns
/// <see cref="IProjectScaleSettings"/>, modules must not reference the
/// engine — so the adapter lives here, where both references are allowed,
/// and the host swaps it in for <c>InMemoryProjectScaleSettings</c> at
/// registration time. Reads are pure-memory (the interface is
/// synchronous): the settings store's snapshot cache — kept warm by the
/// refresh loop and replaced on every write — is the source, and options
/// defaults answer when a project has no cached row yet (settings live
/// reload: a PUT is visible on the next supervisor pass, no restart).
/// </summary>
/// <param name="settings"></param>
/// <param name="scaleOptions"></param>
public sealed class ProjectScaleSettingsAdapter(
    IProjectSettingsStore settings,
    IOptions<ScaleSupervisorOptions> scaleOptions) : IProjectScaleSettings
{
    /// <inheritdoc />
    public ProjectScaleSettings Get(ProjectId projectId)
    {
        return settings.GetCached(projectId) is { } cached
            ? ProjectScaleSettingsFactory.FromRow(cached, scaleOptions.Value)
            : ProjectScaleSettingsFactory.Defaults(scaleOptions.Value);
    }

    /// <summary>
    /// Writes go through the projects settings API
    /// (<c>UpdateSettingsCommand</c> → <c>IProjectSettingsStore</c>), which
    /// refreshes the cache this adapter reads — there is no in-process
    /// override path anymore.
    /// </summary>
    /// <param name="projectId"></param>
    /// <param name="projectSettings"></param>
    /// <exception cref="NotSupportedException">Always — use the settings API.</exception>
    public void Override(ProjectId projectId, ProjectScaleSettings projectSettings)
    {
        throw new NotSupportedException(
            "project scale settings are written through the projects settings API (UpdateSettingsCommand)");
    }
}

/// <summary>The one translation between settings rows / supervisor options and the engine shape.</summary>
file static class ProjectScaleSettingsFactory
{
    public static ProjectScaleSettings FromRow(
        Modules.Projects.Domain.Settings.ProjectSettings row,
        ScaleSupervisorOptions options)
    {
        // null idle TTL / image / git ref mean "engine default" — the
        // supervisor falls back to the options values
        return new ProjectScaleSettings(
            row.MinIdle,
            row.MaxConcurrent,
            row.IdleTtlSeconds is { } idleTtlSeconds ? TimeSpan.FromSeconds(idleTtlSeconds) : options.IdleTtl);
    }

    public static ProjectScaleSettings Defaults(ScaleSupervisorOptions options)
    {
        return new ProjectScaleSettings(options.MinIdle, options.MaxConcurrent, options.IdleTtl);
    }
}
