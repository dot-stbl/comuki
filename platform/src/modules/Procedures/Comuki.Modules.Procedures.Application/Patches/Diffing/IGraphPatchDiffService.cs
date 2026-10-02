using Comuki.Modules.Procedures.Domain.Patches.Diff;

namespace Comuki.Modules.Procedures.Application.Patches.Diffing;

/// <summary>
/// Application-layer seam for the GraphPatch flow: looks up the base
/// compiled version in the version store, runs the domain validator
/// and diff computer, and returns the result. The domain types stay
/// pure (no I/O); this port is the only place the version store
/// participates in the patch flow at task 3.1. Publication (task 3.2)
/// adds the human-approval step on top of the same seam.
/// </summary>
public interface IGraphPatchDiffService
{
    /// <summary>
    /// Computes the semantic <see cref="GraphPatchDiff"/>
    /// of <paramref name="patch"/> against the compiled version pinned by
    /// <c>patch.BaseVersionId</c>. Throws
    /// <see cref="GraphPatchException"/>
    /// with code <c>procedures.patch.invalid_base_version</c> when the
    /// base id does not match any compiled version for the (project,
    /// procedureKey) pair, or with one of the validation codes when the
    /// patch references ids absent from its base.
    /// </summary>
    /// <param name="patch">The patch whose diff to compute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GraphPatchDiff> ComputeDiffAsync(
        Domain.Patches.GraphPatch patch,
        CancellationToken cancellationToken = default);
}
