using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Host.Workers;

/// <summary>
/// Source-git enrichment of a claim (harden-pi-worker-sandbox 4.3):
/// reads the claimed item's project and its settings, and resolves the
/// optional HTTPS credential for <c>Project.SourceGitUrl</c>. The claim
/// loop hands the result to the worker verbatim — the worker clones the
/// URL after claim, before the agent starts.
/// <para>
/// A credential ref that fails to resolve (unset env var, missing file)
/// does not fail the claim: the resolver logs a warning and returns a
/// <c>null</c> credential, and the anonymous clone of a private
/// repository fails on the worker — the spec's "private without a
/// credential fails the item" outcome, reached through the natural path
/// instead of a claim-time special case.
/// </para>
/// </summary>
/// <param name="projectStore">Projects module read port.</param>
/// <param name="settingsStore">Project settings read port (live-reload).</param>
/// <param name="secretResolver">Host secret resolver for the settings' git credential ref.</param>
/// <param name="logger">Structured logger.</param>
public sealed class ClaimSourceGitResolver(
    IProjectStore projectStore,
    IProjectSettingsStore settingsStore,
    ISecretResolver secretResolver,
    ILogger<ClaimSourceGitResolver> logger)
{
    /// <summary>
    /// Reads the project's source-git fields and resolves the optional
    /// credential. A missing project resolves to all-null (the worker
    /// fails the item with "missing SourceGitUrl").
    /// </summary>
    /// <param name="projectId">Project the claimed run belongs to.</param>
    /// <param name="cancellationToken"></param>
    public async Task<ClaimSourceGit> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var id = new ProjectId(projectId);
        if (await projectStore.FindByIdAsync(id, cancellationToken) is not { SourceGitUrl: { } url } project
            || string.IsNullOrWhiteSpace(url))
        {
            return new ClaimSourceGit(null, null, null);
        }

        var settings = await settingsStore.FindAsync(id, cancellationToken);
        string? credential = null;
        if (!string.IsNullOrWhiteSpace(settings?.GitCredentialRef))
        {
            try
            {
                credential = await secretResolver.ResolveAsync(settings.GitCredentialRef, cancellationToken);
            }
            catch (Exception exception) when (exception is SecretRefUnsetException or SecretRefFormatException)
            {
                logger.LogWarning(
                    exception,
                    "Git credential ref of project {ProjectId} could not be resolved — cloning will proceed anonymously",
                    projectId);
            }
        }

        return new ClaimSourceGit(url, project.SourceGitRef, credential);
    }
}

/// <summary>Claim source-git enrichment result: what the worker clones and with what credential.</summary>
/// <param name="SourceGitUrl">Product repository HTTPS URL; <c>null</c> when the project has none.</param>
/// <param name="SourceGitRef">Branch or tag; <c>null</c> = default branch.</param>
/// <param name="GitCredential">Resolved HTTPS credential; <c>null</c> for anonymous clones.</param>
public sealed record ClaimSourceGit(string? SourceGitUrl, string? SourceGitRef, string? GitCredential);
