using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Views;

namespace Comuki.Modules.Projects.Application.Settings.Update;

/// <summary>
/// Replaces the settings of a project. The mutation flows through the
/// domain entity (which bumps the version); the store re-checks the
/// version and lets the DB concurrency token catch writer races. Live
/// reload: the store refreshes the shared snapshot cache on save, so every
/// reader — including the compute adapter — observes the change without a
/// restart.
/// </summary>
/// <param name="settings"></param>
/// <param name="clock"></param>
/// <param name="mapper">Projects the saved entity onto its API view.</param>
public sealed class UpdateSettingsHandler(IProjectSettingsStore settings, TimeProvider clock, IProjectsMapper mapper)
{
    /// <summary>Updates the settings.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <exception cref="ProjectNotFoundException">No settings row for the project.</exception>
    /// <exception cref="ProjectSettingsConflictException">The presented version is stale.</exception>
    public async Task<ProjectSettingsView> HandleAsync(
        UpdateSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await settings.FindAsync(command.ProjectId, cancellationToken)
            ?? throw new ProjectNotFoundException(command.ProjectId);

        if (current.Version != command.Version)
        {
            throw new ProjectSettingsConflictException(command.ProjectId, command.Version, current.Version);
        }

        current.Apply(
            command.MinIdle,
            command.MaxConcurrent,
            command.IdleTtlSeconds,
            command.ApproveRequired,
            command.KnowledgeEnabled,
            command.VerifyEnabled,
            command.ProxyEnabled,
            command.SoftBudgetUsdMicros,
            command.HardBudgetUsdMicros,
            command.DomainType,
            command.CustomDomainTypesJson,
            command.GitCredentialRef,
            clock.GetUtcNow());

        var saved = await settings.SaveAsync(current, cancellationToken);

        return mapper.ToView(saved);
    }
}
