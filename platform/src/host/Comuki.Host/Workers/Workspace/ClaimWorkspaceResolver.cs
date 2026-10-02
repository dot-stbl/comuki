using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;

namespace Comuki.Host.Workers.Workspace;

/// <summary>
/// Resolves the workspace facts of a claimed item's project:
/// <c>SourceGitUrl</c> / <c>SourceGitRef</c> from the project row and the
/// git credential from the project settings' secret reference, resolved
/// server-side so the worker never sees the reference syntax. A project
/// without <c>SourceGitUrl</c> yields <c>null</c> — the claim then omits
/// the workspace fields and the Translator's prepare step fails the item
/// with a typed workspace error (the failure belongs to prepare, not to
/// the claim).
/// <para>
/// An unresolvable or malformed credential reference degrades to "no
/// credential" with a warning instead of failing the claim: the item was
/// already leased by then, and a 500 would strand it until the reaper
/// takes it. The unauthenticated clone of a private repository fails at
/// prepare, which is the spec's failure point.
/// </para>
/// </summary>
/// <param name="projects">Project lookup — the source URL/ref live on the project row.</param>
/// <param name="settings">Settings lookup — the credential reference lives on the settings row.</param>
/// <param name="secrets">Resolves the credential reference to its value; the value never leaves this call.</param>
/// <param name="logger">Structured logger; logs reference failures without the resolved values.</param>
public sealed class ClaimWorkspaceResolver(
    IProjectStore projects,
    IProjectSettingsStore settings,
    ISecretResolver secrets,
    ILogger<ClaimWorkspaceResolver> logger)
{
    /// <summary>
    /// Resolves the workspace facts for a claimed item's project; null when
    /// the project has no source repository configured (or the project row
    /// is gone).
    /// </summary>
    /// <param name="projectId">Project of the claimed item's parent run.</param>
    /// <param name="cancellationToken"></param>
    public async Task<ClaimWorkspace?> ResolveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (await projects.FindByIdAsync(new ProjectId(projectId), cancellationToken) is not { } project
            || string.IsNullOrWhiteSpace(project.SourceGitUrl))
        {
            return null;
        }

        var credential = (string?)null;
        if ((await settings.FindAsync(project.Id, cancellationToken))?.GitCredentialRef is { } reference
            && !string.IsNullOrWhiteSpace(reference))
        {
            try
            {
                credential = await secrets.ResolveAsync(reference, cancellationToken);
            }
            catch (SecretRefUnsetException exception)
            {
                logger.LogWarning(
                    exception,
                    "Git credential reference of project {ProjectId} is set but unresolved — cloning without a credential",
                    project.Id);
            }
            catch (SecretRefFormatException exception)
            {
                logger.LogWarning(
                    exception,
                    "Git credential reference of project {ProjectId} is malformed — cloning without a credential",
                    project.Id);
            }
        }

        return new ClaimWorkspace(project.SourceGitUrl, project.SourceGitRef, credential);
    }
}
