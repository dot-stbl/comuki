using System.Reflection;
using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Catalog.Keys;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Registry;
using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// Boot-time partitioning of <see cref="IComukiWorker"/> registrations
/// into the included vs deferred buckets the
/// <see cref="ComukiWorkerRegistry"/> loop hosts. One
/// <see cref="RequiresFeatureAttribute"/> on an
/// <see cref="IComukiWorker"/> implementation is the only signal
/// consulted; workers without the attribute skip the edition entirely
/// (the cheap common case — every existing production worker falls in
/// this bucket because they implement Community-baseline
/// functionality and gating one would revoke Community behavior
/// without a product decision).
/// </summary>
/// <remarks>
/// Reads <c>RequiresFeatureAttribute</c> once per registration at
/// composition time via <c>MemberInfo.GetCustomAttribute</c> —
/// reflection is bounded at boot (small worker count) and cached by
/// the runtime, so a per-call lookup is fine. The returned
/// <see cref="Feature"/> is the same instance the runtime
/// <see cref="EditionGate.EvaluateFeature"/> uses; <see cref="IEdition.Has(Feature)"/>
/// resolves it via the license path the gate already does.
/// <para>
/// A missing <see cref="IEdition"/> for a GATED marker is treated as a
/// wiring gap (mirrors <see cref="AddComukiModuleExtensions.AddComukiModule{TModule}"/>'s
/// composition-time check) — fail loud at boot, not silently with a
/// "worker not registered" symptom in production.
/// </para>
/// </remarks>
internal static class WorkerFeatureGate
{
    /// <summary>
    /// One partition produced by <see cref="Partition"/>: ungated
    /// workers run immediately, covered gated workers run
    /// immediately, and deferred gated workers wait for the
    /// supervisor loop to re-check coverage on
    /// <see cref="ComukiWorkerRegistry.DeferredRecheckInterval"/>.
    /// </summary>
    /// <param name="Ungated">Workers with no attribute — always run; no edition consultation, no recheck.</param>
    /// <param name="Covered">Gated workers the current edition already covers — run from boot.</param>
    /// <param name="Deferred">Gated workers that are not yet covered; the registry re-checks their feature on an interval.</param>
    public sealed record Partition(
        IReadOnlyList<IComukiWorker> Ungated,
        IReadOnlyList<DeferredWorker> Covered,
        IReadOnlyList<DeferredWorker> Deferred);

    /// <summary>
    /// One gated worker — held outside the registry's dictionary
    /// until its feature becomes covered, then promoted into the
    /// running set under the same lock as <see cref="ComukiWorkerRegistry"/>'s
    /// other dictionary mutations.
    /// </summary>
    /// <param name="Worker">The worker instance.</param>
    /// <param name="Feature">The resolved feature; equality with the registry's catalog is guaranteed because <see cref="IEdition.Has(Feature)"/> consults the same catalog.</param>
    /// <param name="FeatureKey">The literal attribute string, kept for the boot-time log line and any future diagnostic surface.</param>
    public sealed record DeferredWorker(IComukiWorker Worker, Feature Feature, string FeatureKey);

    /// <summary>
    /// Partitions <paramref name="workers"/> by [RequiresFeature] coverage
    /// against <paramref name="edition"/>. Workers without the
    /// attribute go straight to <see cref="Partition.Ungated"/> with
    /// no edition consultation (the cheap common case). A gated
    /// marker with no edition or with an unresolvable feature key
    /// throws synchronously — that is a wiring gap, not a soft skip.
    /// </summary>
    /// <param name="workers">Every <see cref="IComukiWorker"/> registration; order is preserved in each output bucket.</param>
    /// <param name="edition">Runtime <see cref="IEdition"/>; null when the editions layer is not wired (a wiring gap for any gated marker).</param>
    /// <param name="logger">Sink for the one-per-worker "deferred" log line.</param>
    /// <exception cref="InvalidOperationException">
    /// A worker carries <see cref="RequiresFeatureAttribute"/> but
    /// <paramref name="edition"/> is null (wiring gap) or its key is
    /// malformed / not present in the catalog (registry gap). Both
    /// would otherwise be silent skips; fail loud here so the gap is
    /// visible at boot.
    /// </exception>
    public static Partition PartitionWorkers(
        IEnumerable<IComukiWorker> workers,
        IEdition? edition,
        ILogger logger)
    {
        // The registry is built once at boot; constructing a fresh
        // catalog-resolution helper inline here mirrors
        // AddComukiModule's precedent for the same shape (no DI
        // injection of the registry at this layer — the runtime
        // EditionGate reads IEditionCapabilityRegistry from DI, this
        // boot-time path is intentionally a single shot).
        var catalog = new EditionCapabilityRegistry();

        var ungated = new List<IComukiWorker>();
        var covered = new List<DeferredWorker>();
        var deferred = new List<DeferredWorker>();

        foreach (var worker in workers)
        {
            var attribute = worker.GetType().GetCustomAttribute<RequiresFeatureAttribute>();
            if (attribute is null)
            {
                ungated.Add(worker);
                continue;
            }

            // Wiring gap, not a soft skip — mirrors AddComukiModule's
            // composition-time check.
            if (edition is null)
            {
                throw new InvalidOperationException(
                    $"worker '{worker.Name}' ({worker.GetType().FullName}) carries a "
                    + $"RequiresFeatureAttribute('{attribute.FeatureKey}') but no IEdition was registered; "
                    + "wire AddComukiEditions(...) before AddComukiWorkers() so the "
                    + "edition gating layer is available at boot.");
            }

            var featureKey = attribute.FeatureKey;
            if (!FeatureKey.IsWellFormed(featureKey)
                || !catalog.TryGetFeature(FeatureKey.Parse(featureKey), out var resolved)
                || resolved is null)
            {
                throw new InvalidOperationException(
                    $"worker '{worker.Name}' ({worker.GetType().FullName}) carries a "
                    + $"RequiresFeatureAttribute with a non-resolvable feature key '{featureKey}'; "
                    + "the key must exist in the editions registry.");
            }

            if (edition.Has(resolved))
            {
                covered.Add(new DeferredWorker(worker, resolved, featureKey));
            }
            else
            {
                logger.LogWarning(
                    "worker {WorkerName} deferred: edition does not cover feature {FeatureKey}",
                    worker.Name,
                    featureKey);
                deferred.Add(new DeferredWorker(worker, resolved, featureKey));
            }
        }

        return new Partition(ungated, covered, deferred);
    }
}
