using Comuki.Modules.Procedures.Domain.Layering.Model;
using Comuki.Modules.Procedures.Domain.Layering.Results;

namespace Comuki.Modules.Procedures.Domain.Layering.Helpers;

/// <summary>
/// The merge's set and floor computations, extracted per the
/// no-private-methods rule. Pure functions over their arguments —
/// the floors merge refuses loosening overrides loudly before
/// returning the effective <see cref="ApprovalFloors"/>.
/// </summary>
internal static class LayeringMerge
{
    /// <summary>
    /// Unions the platform's allowlist with the project's additional
    /// kind keys. The platform never grants; the project may extend
    /// (with a kind the platform already publishes).
    /// </summary>
    /// <param name="platform">The platform's default allowlist.</param>
    /// <param name="project">The project's policy with optional additions.</param>
    /// <returns>The merged allowlist, ordinal-compared.</returns>
    public static HashSet<string> MergeAllowedKindKeys(
        PlatformDefaults platform,
        ProjectPolicy project)
    {
        var allowed = new HashSet<string>(platform.AllowedKindKeys, StringComparer.Ordinal);
        if (project.AdditionalAllowedKindKeys is not null)
        {
            foreach (var key in project.AdditionalAllowedKindKeys)
            {
                allowed.Add(key);
            }
        }

        return allowed;
    }

    /// <summary>
    /// Computes the effective repair-boundary and human-gate floors:
    /// the platform's defaults tightened by the project's overrides.
    /// A project cannot loosen the platform's floors — the loosening
    /// attempt is a loud refusal. The compile gate (task 2.3) reads
    /// these as the effective bounds when validating repair-boundary
    /// and human-gate node declarations.
    /// </summary>
    /// <param name="platform">The platform's default floors.</param>
    /// <param name="project">The project's policy with optional overrides.</param>
    /// <returns>The effective floors.</returns>
    public static ApprovalFloors MergeFloors(
        PlatformDefaults platform,
        ProjectPolicy project)
    {
        LayeringRefusals.RefuseProjectLooseningMaxGenerations(platform, project);
        LayeringRefusals.RefuseProjectLooseningMinApprovals(platform, project);

        return new ApprovalFloors(
            project.MaxGenerationsOverride ?? platform.MaxGenerations,
            project.MinApprovalsOverride ?? platform.MinApprovals);
    }
}
