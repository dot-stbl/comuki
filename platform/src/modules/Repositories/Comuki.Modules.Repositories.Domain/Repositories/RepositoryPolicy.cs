using Comuki.Modules.Repositories.Domain.Ids;

namespace Comuki.Modules.Repositories.Domain.Repositories;

/// <summary>
/// Per-Repository snapshot of the rules the host API exposes — protected
/// branches, required checks, and approvers. One row per Repository (the PK
/// is the Repository id, mirroring how <c>DomainTypeAdmission</c> in the
/// Projects module keys policy rows by their owning project). The lists
/// are stored as native Postgres <c>text[]</c> and normalized to a
/// deterministic order so two snapshots of the same source don't churn on
/// every refresh.
/// </summary>
public sealed class RepositoryPolicy
{
    internal RepositoryPolicy()
    {
    }

    /// <summary>FK + PK of this policy row; the matching <c>Repository</c> row owns it.</summary>
    public RepositoryId RepositoryId { get; private set; }

    /// <summary>Branches that the merge queue protects from direct push; empty means "no branch protection recorded".</summary>
    public string[] ProtectedBranches { get; private set; } = [];

    /// <summary>Check names that must be green before merge; empty means "no required checks recorded".</summary>
    public string[] RequiredChecks { get; private set; } = [];

    /// <summary>User/team handles with merge approval; empty means "no approvers recorded".</summary>
    public string[] Approvers { get; private set; } = [];

    /// <summary>When the snapshot was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last refresh timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a policy snapshot from host-API data. Each list is normalized
    /// (trim, lower-invariant, drop empties, deduplicate, sort ordinal) so
    /// the persisted arrays are deterministic.
    /// </summary>
    /// <param name="repositoryId">Owning Repository id.</param>
    /// <param name="protectedBranches">Branch names the host API reports as protected.</param>
    /// <param name="requiredChecks">Check names the host API reports as required.</param>
    /// <param name="approvers">User/team handles the host API reports as approvers.</param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static RepositoryPolicy Create(
        RepositoryId repositoryId,
        IEnumerable<string> protectedBranches,
        IEnumerable<string> requiredChecks,
        IEnumerable<string> approvers,
        DateTimeOffset now)
    {
        return new RepositoryPolicy
        {
            RepositoryId = repositoryId,
            ProtectedBranches = PolicyKeys.NormalizeList(protectedBranches),
            RequiredChecks = PolicyKeys.NormalizeList(requiredChecks),
            Approvers = PolicyKeys.NormalizeList(approvers),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Refreshes the snapshot from a fresh host-API fetch. Each list
    /// argument is required (not nullable PATCH) because the host API is
    /// the source of truth — passing null would mean "we don't know", and
    /// that state is indistinguishable from "no policy exists", which a
    /// later row would have to disambiguate. Empty list means "the host
    /// reports no entries".
    /// </summary>
    /// <param name="protectedBranches"></param>
    /// <param name="requiredChecks"></param>
    /// <param name="approvers"></param>
    /// <param name="now"></param>
    public void Replace(
        IEnumerable<string> protectedBranches,
        IEnumerable<string> requiredChecks,
        IEnumerable<string> approvers,
        DateTimeOffset now)
    {
        ProtectedBranches = PolicyKeys.NormalizeList(protectedBranches);
        RequiredChecks = PolicyKeys.NormalizeList(requiredChecks);
        Approvers = PolicyKeys.NormalizeList(approvers);
        UpdatedAt = now;
    }
}

/// <summary>
/// Key normalization shared by <see cref="RepositoryPolicy"/>'s factory
/// and replacer. File-scoped so the entity keeps a single public surface
/// without a private helper method (<c>code-shape.md</c> §9).
/// </summary>
file static class PolicyKeys
{
    /// <summary>
    /// Normalizes a list of policy keys: trim + lower-case each entry, drop
    /// blanks, deduplicate, then sort ordinal so two snapshots of the same
    /// source land as identical arrays. Returns <c>string[]</c> so Npgsql
    /// maps it straight onto a <c>text[]</c> column without an
    /// intermediate <see cref="List{T}"/> round-trip.
    /// </summary>
    /// <param name="keys"></param>
    /// <returns></returns>
    public static string[] NormalizeList(IEnumerable<string> keys)
    {
        return
        [
            .. keys
                .Select(Repository.Normalize)
                .Where(static key => key.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal),
        ];
    }
}
