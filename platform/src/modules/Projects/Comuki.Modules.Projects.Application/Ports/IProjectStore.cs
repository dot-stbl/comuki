using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Ports;

/// <summary>
/// Persistence port for projects. Implemented by the module infrastructure
/// over its DbContext; Application code never touches EF. <see cref="AddAsync"/>
/// persists a project together with its default settings row in one unit of
/// work — a project without settings cannot exist through the write paths.
/// </summary>
public interface IProjectStore
{
    /// <summary>Finds a project by id.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Project?> FindByIdAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>Finds a project by its (normalized, lower-cased) slug.</summary>
    /// <param name="slug"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Project?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Lists projects ordered by creation time; archived ones unless <paramref name="includeArchived"/>.</summary>
    /// <param name="includeArchived"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current count of projects matching <paramref name="includeArchived"/>,
    /// materialised as a single scalar — never the full row set. Used by
    /// the gate and the transactional handler enforcement so neither
    /// path has to allocate an <c>IReadOnlyList&lt;Project&gt;</c> only
    /// to take its <c>.Count</c> afterwards. The implementation runs the
    /// predicate that <see cref="ListAsync"/> would have applied
    /// (<c>WHERE NOT archived</c> when <paramref name="includeArchived"/>
    /// is false) directly against the table.
    /// </summary>
    /// <param name="includeArchived">When <c>true</c>, archived projects are counted too.</param>
    /// <param name="cancellationToken"></param>
    public Task<int> CountAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>Persists a new project and its default settings row atomically.</summary>
    /// <param name="project"></param>
    /// <param name="settings"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task AddAsync(Project project, ProjectSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new project + settings row under the authoritative
    /// count-quota enforcement: takes a transaction-scoped advisory lock
    /// (<c>pg_advisory_xact_lock(hashtext('limit:projects'))</c>) on the
    /// connection, counts the current non-archived projects in the same
    /// transaction, and either inserts (returning <c>true</c>) or refuses
    /// (returning <c>false</c>). Two concurrent calls at the cap
    /// therefore serialise: the first commits, the second sees the
    /// first's insert and refuses. The request-time filter fast-fail is
    /// advisory only — this is the source of truth.
    /// </summary>
    /// <param name="project">The new project aggregate to persist.</param>
    /// <param name="settings">The default settings row created with the project.</param>
    /// <param name="cap">The effective cap from <see cref="Shared.Editions.Edition.IEdition.Limit"/>. A cap &lt;= 0 fails closed without touching the database.</param>
    /// <param name="cancellationToken"></param>
    /// <returns><c>true</c> when the insert succeeded; <c>false</c> when the count met or exceeded <paramref name="cap"/>.</returns>
    public Task<bool> TryInsertWithProjectLimitAsync(
        Project project,
        ProjectSettings settings,
        int cap,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new or changed project.</summary>
    /// <param name="project"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task SaveAsync(Project project, CancellationToken cancellationToken = default);
}
