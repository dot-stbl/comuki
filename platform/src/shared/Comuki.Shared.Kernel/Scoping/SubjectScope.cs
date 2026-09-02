using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Kernel.Scoping;

/// <summary>
/// The object axis of the authorization model: which projects the current
/// subject may see. One value per asynchronous flow, established once
/// (authentication for requests, <see cref="ISubjectScopeAccessor.AsSystem"/>
/// for background consumers) and read by the global query filters — never
/// recomputed inside a query.
/// </summary>
/// <param name="Unrestricted">Whether the subject bypasses project scoping — any platform-scope role, or a system consumer.</param>
/// <param name="SystemName">The kebab-case consumer name when this scope was declared by a system consumer; null for human subjects.</param>
/// <param name="ProjectIds">Projects the subject is confined to. Empty means "no project", not "any project" — the fail-closed answer.</param>
public sealed record SubjectScope(
    bool Unrestricted,
    string? SystemName,
    IReadOnlyList<ProjectId> ProjectIds)
{
    /// <summary>The fail-closed scope: an authenticated subject with no assignments matches no rows.</summary>
    public static readonly SubjectScope Nothing = new(false, null, []);

    /// <summary>
    /// The system scope of a background consumer: unrestricted, carrying
    /// the consumer's name so later audit reads can attribute the work.
    /// </summary>
    /// <param name="consumerName">The consumer's kebab-case name (<c>lease-reaper</c>, <c>worker-runtime</c>).</param>
    public static SubjectScope ForSystem(string consumerName)
    {
        return new(true, consumerName, []);
    }

    /// <summary>
    /// Whether the subject may act on data owned by the given project —
    /// unrestricted covers every project; otherwise the project must be
    /// one the assignments reach. A subject with no assignments matches
    /// nothing, which is the fail-closed answer.
    /// </summary>
    /// <param name="project">The project owning the object (or named by the URL path segment).</param>
    public bool Allows(ProjectId project)
    {
        return Unrestricted || ProjectIds.Contains(project);
    }
}
