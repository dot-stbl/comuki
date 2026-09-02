using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Entity → view mapping, hand-written and pure (no DI, no source
/// generator — the module has no Mapperly toolchain). One home for every
/// projection so handlers stay one-liners.
/// </summary>
public static class ProjectMapper
{
    /// <summary>Maps a project entity to its read model.</summary>
    /// <param name="project"></param>
    /// <returns></returns>
    public static ProjectView ToView(Project project)
    {
        return new ProjectView(
            project.Id,
            project.Name,
            project.Slug,
            project.Description,
            project.ProfilesGitUrl,
            project.ProfilesGitRef,
            project.Archived,
            project.ArchivedAt,
            project.CreatedAt,
            project.UpdatedAt);
    }

    /// <summary>Maps a settings entity to its read model.</summary>
    /// <param name="settings"></param>
    /// <returns></returns>
    public static ProjectSettingsView ToView(ProjectSettings settings)
    {
        return new ProjectSettingsView(
            settings.ProjectId,
            settings.MinIdle,
            settings.MaxConcurrent,
            settings.IdleTtlSeconds,
            settings.ApproveRequired,
            settings.KnowledgeEnabled,
            settings.VerifyEnabled,
            settings.ProxyEnabled,
            settings.SoftBudgetUsdMicros,
            settings.HardBudgetUsdMicros,
            settings.UpdatedAt,
            settings.Version);
    }
}
