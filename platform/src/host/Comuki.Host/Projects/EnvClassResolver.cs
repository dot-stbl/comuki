using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Projects;

/// <summary>
/// Reads <c>Project.EnvClass</c> through the projects module's
/// <see cref="IProjectStore"/> port — keeps modules decoupled (no module
/// references Projects directly; the host bridges). An empty string is
/// returned when the project has no class — the work item is then
/// permanently unclaimable, exactly as documented in the
/// add-worker-environments work-queue spec ("Unconfirmed class is not
/// claimable"). A missing project is an invariant violation and throws
/// <see cref="InvalidOperationException"/> — the caller names the flow.
/// </summary>
internal static class EnvClassResolver
{
    /// <summary>Resolves the project's bound env class; empty when unset.</summary>
    /// <param name="projects">Projects module port (host bridge).</param>
    /// <param name="projectId">Project to read.</param>
    /// <param name="flowName">Calling flow for the exception message (e.g. <c>chat plan apply</c>).</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">The project row is gone.</exception>
    public static async Task<string> ResolveAsync(
        IProjectStore projects,
        ProjectId projectId,
        string flowName,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindByIdAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"{flowName}: project {projectId.Value} not found while stamping env class");
        return project.EnvClass ?? string.Empty;
    }
}
