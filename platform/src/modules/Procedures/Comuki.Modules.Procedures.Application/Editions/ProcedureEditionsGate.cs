using Comuki.Modules.Procedures.Domain.Editions;

namespace Comuki.Modules.Procedures.Application.Editions;

/// <summary>
/// Enforcement of the editions split at the procedure level (design
/// decision 8): the mechanism ships to community with caps (3 published
/// procedures per project, 5 concurrent pinned runs), and paid editions
/// lift them. Multi-repo bindings require the multi-repo feature key.
/// </summary>
public static class ProcedureEditionsGate
{
    /// <summary>Community cap for published procedures per project.</summary>
    public const int CommunityPublishedPerProject = 3;

    /// <summary>Community cap for concurrent pinned runs.</summary>
    public const int CommunityConcurrentPinnedRuns = 5;

    /// <summary>Feature key for multi-repo bindings.</summary>
    public const string MultiRepoFeatureKey = "procedures.multi-repo";

    /// <summary>
    /// Refuses publication when the project is at its published-procedure
    /// cap. The community edition caps at 3; paid editions return a
    /// higher (or absent) limit and the check passes.
    /// </summary>
    /// <param name="currentPublished">How many procedures the project has published.</param>
    /// <param name="limit">The effective limit from the edition (null = unlimited).</param>
    public static void CheckPublishedLimit(int currentPublished, int? limit)
    {
        if (limit is { } effectiveLimit && currentPublished >= effectiveLimit)
        {
            throw new ProcedureEditionsException(
                ProcedureEditionsException.PublishedLimitExceeded,
                $"Project has {currentPublished} published procedures; the effective edition caps at {effectiveLimit}. Refusing publication.");
        }
    }

    /// <summary>
    /// Refuses admission when the project is at its concurrent-pinned-run
    /// cap. The community edition caps at 5; paid editions lift it.
    /// </summary>
    /// <param name="currentPinnedRuns">How many pinned runs are currently active.</param>
    /// <param name="limit">The effective limit from the edition (null = unlimited).</param>
    public static void CheckConcurrentPinnedRuns(int currentPinnedRuns, int? limit)
    {
        if (limit is { } effectiveLimit && currentPinnedRuns >= effectiveLimit)
        {
            throw new ProcedureEditionsException(
                ProcedureEditionsException.ConcurrentRunsLimitExceeded,
                $"Project has {currentPinnedRuns} concurrent pinned runs; the effective edition caps at {effectiveLimit}. Refusing admission.");
        }
    }

    /// <summary>
    /// Refuses a cross-repository binding when the multi-repo feature key
    /// is not granted. Community edition does not grant it; paid does.
    /// </summary>
    /// <param name="bindingCount">How many repositories the procedure binds (1 = single-repo, always allowed).</param>
    /// <param name="granted">The effective edition's granted feature keys.</param>
    public static void CheckMultiRepoBinding(int bindingCount, GrantedFeatureKeys granted)
    {
        if (bindingCount <= 1)
        {
            return;
        }

        if (!granted.IsGranted(MultiRepoFeatureKey))
        {
            throw new ProcedureEditionsException(
                ProcedureEditionsException.MultiRepoNotGranted,
                $"Cross-repository binding ({bindingCount} repositories) requires feature '{MultiRepoFeatureKey}' which the effective edition does not grant. Refusing binding.");
        }
    }
}
