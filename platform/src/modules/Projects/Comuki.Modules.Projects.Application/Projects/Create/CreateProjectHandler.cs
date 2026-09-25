using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Projects.Application.Projects.Create;

/// <summary>
/// Creates a project and its default settings row under the authoritative
/// project-count quota enforcement. The duplicate-slug check stays as a
/// pre-flight (the unique index still backs it as a last-line of defence
/// — a concurrent create with the same slug loses with a DB error
/// instead of a dupe). The project-count limit check is transactional:
/// <see cref="IProjectStore.TryInsertWithProjectLimitAsync"/> takes a
/// <c>pg_advisory_xact_lock</c>, counts inside the same transaction, and
/// refuses with a 403-mappable <see cref="ProviderForbiddenException"/>
/// carrying the <c>edition.limit_exceeded</c> code. Two concurrent
/// writers at the cap serialise on the lock; only one commits.
/// </summary>
/// <param name="projects">Project persistence port.</param>
/// <param name="edition">The runtime read-side of the current license — supplies the cap.</param>
/// <param name="clock">Time source for the project's created/updated timestamps.</param>
public sealed class CreateProjectHandler(
    IProjectStore projects,
    IEdition edition,
    TimeProvider clock)
{
    /// <summary>Creates the project.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The created project's view.</returns>
    /// <exception cref="ProjectConflictException">The slug is already taken.</exception>
    /// <exception cref="ProviderForbiddenException">The current edition's project cap is exhausted. The handler is the authoritative cap check (the request-time <c>[EnforceLimit]</c> filter is advisory only).</exception>
    public async Task<ProjectView> HandleAsync(CreateProjectCommand command, CancellationToken cancellationToken = default)
    {
        var slug = command.Slug.Trim().ToLowerInvariant();
        if (await projects.FindBySlugAsync(slug, cancellationToken) is not null)
        {
            throw new ProjectConflictException($"project slug '{slug}' is already taken");
        }

        var now = clock.GetUtcNow();
        var project = Project.Create(
            command.Name,
            slug,
            command.Description,
            command.ProfilesGitUrl,
            command.ProfilesGitRef,
            now);

        var cap = edition.Limit(Limits.Projects);
        var settings = ProjectSettings.CreateDefaults(project.Id, now);

        // Authoritative count-quota enforcement: opens its own transaction,
        // takes the project-limit advisory lock, counts inside the same
        // transaction, inserts (and commits) or refuses (and rolls back).
        // Two concurrent writers at the cap serialise on the lock.
        if (!await projects.TryInsertWithProjectLimitAsync(project, settings, cap, cancellationToken))
        {
            // Re-read the current count for the problem detail — the
            // store refused without committing, so the count we saw
            // here is the post-first-writer snapshot.
            var current = await projects.CountAsync(includeArchived: false, cancellationToken);
            throw new ProviderForbiddenException(
                code: "edition.limit_exceeded",
                message: $"limit 'projects' is exhausted ({current}/{cap})");
        }

        return ProjectMapper.ToView(project);
    }
}
