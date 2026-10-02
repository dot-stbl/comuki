using Comuki.Engine.Compute.Environments.Catalog;
using Comuki.Shared.Contracts.Admission;

namespace Comuki.Engine.Compute.Admission;

/// <summary>
/// v1 implementation of <see cref="ISlotAdmissionEvaluator"/> (worker-admission
/// task 1.1). Runs the six ordered checks and stops at the first deny, returning
/// a typed <see cref="SlotAdmissionResult"/>.
///
/// <para>
/// v1 wires checks (1) confirmed env class and (2) fleet publisher allowlist
/// against the existing <see cref="IEnvironmentCatalog"/> — both already
/// expose the seams this evaluator reads. Checks (3) advertised capacity,
/// (4) isolation class the host can honor, (5) edition coverage, and (6)
/// secret-ref resolvability are <b>sibling hooks</b>: each returns pass on
/// this slice and is replaced by its real implementation as the
/// corresponding change lands. The result type carries the
/// <see cref="AdmissionCodes"/> so callers stay stable when a real
/// check lands.
/// </para>
///
/// <para>
/// <b>Why pass-by-default on the unbuilt hooks.</b> The admission record
/// must already be a hard prerequisite for <c>StartAsync</c> and pi spawn
/// (worker-admission spec §"SlotAdmission is the only start input"), but
/// only the checks with implementations today can refuse a request. Wiring
/// the full six-ordered gate in one PR would couple this change to four
/// unrelated changes — out of scope for task 1.1. The hook surface
/// (<see cref="EvaluateCapacityAsync"/> / <see cref="EvaluateIsolationAsync"/>
/// / <see cref="EvaluateEditionAsync"/> / <see cref="EvaluateSecretsAsync"/>)
/// is <c>internal virtual</c> on purpose: each sibling change overrides one
/// and only one — the integration tests at
/// <c>DefaultSlotAdmissionEvaluatorShould</c> assert pass-by-default for the
/// four missing checks so a future implementation cannot silently regress
/// to "deny" when no concrete check exists yet.
/// </para>
/// </summary>
/// <param name="environmentCatalog">Catalog the two wired checks (class / publisher) read against.</param>
public class DefaultSlotAdmissionEvaluator(IEnvironmentCatalog environmentCatalog) : ISlotAdmissionEvaluator
{
    private readonly IEnvironmentCatalog environmentCatalog = environmentCatalog;

    /// <inheritdoc />
    public async Task<SlotAdmissionResult> EvaluateAsync(
        SlotAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check (1) — confirmed env class on the target repository
        // (worker-admission spec §"Unconfirmed class denies before capacity").
        // A missing or empty class is a typed refusal — the catalog map
        // would otherwise consult capacity (3) before noticing the
        // repository never bound a class, and that surfaces as a confusing
        // `admission.capacity` instead of the truthful `env_unconfirmed`.
        if (string.IsNullOrWhiteSpace(request.EnvClass))
        {
            return Deny(AdmissionCodes.EnvUnconfirmed);
        }

        if (!environmentCatalog.TryGet(request.EnvClass, out var bundle) || bundle is null)
        {
            return Deny(AdmissionCodes.EnvUnconfirmed);
        }

        // Check (2) — fleet publisher allowlist (worker-admission spec
        // §"Unallowlisted community bundle is not started"). The catalog
        // knows which publishers the fleet admits; a community / org shelf
        // without an operator opt-in is refused here, before any container
        // is created.
        if (!environmentCatalog.IsAllowed(bundle.Publisher.Value))
        {
            return Deny(AdmissionCodes.Publisher);
        }

        // Check (3) — advertised capacity for the class. Sibling hook:
        // the fleet pool advertisement is wired by the worker-pools slice;
        // until then this returns pass and the rest of the pipeline is
        // gated by the four upstream checks.
        var capacityResult = await EvaluateCapacityAsync(request, bundle, cancellationToken).ConfigureAwait(false);
        if (capacityResult is not null)
        {
            return capacityResult;
        }

        // Check (4) — isolation class the host can honor. Sibling hook
        // landed by `harden-pi-worker-sandbox`; today it always passes.
        var isolationResult = await EvaluateIsolationAsync(request, bundle, cancellationToken).ConfigureAwait(false);
        if (isolationResult is not null)
        {
            return isolationResult;
        }

        // Check (5) — edition coverage for the class/runtime. Sibling hook
        // wired to the editions registry; today `RequestEdition` is honored
        // unconditionally and we always pass.
        var editionResult = await EvaluateEditionAsync(request, bundle, cancellationToken).ConfigureAwait(false);
        if (editionResult is not null)
        {
            return editionResult;
        }

        // Check (6) — secret refs resolvable under the worker subject.
        // Sibling hook landed by `agent-runtime-capabilities`; today an
        // empty ref list passes by default and any non-empty list fails
        // loudly so a slot carrying refs cannot slip through silently
        // until the catalog lookup is wired.
        var secretsResult = await EvaluateSecretsAsync(request, bundle, cancellationToken).ConfigureAwait(false);
        return secretsResult is not null ? secretsResult : Admit();
    }

    /// <summary>
    /// Check (3) — advertised capacity for the class. Returns a deny
    /// <see cref="SlotAdmissionResult"/> when the fleet cannot host the
    /// slot; <c>null</c> when the check passes. The base implementation
    /// returns <c>null</c>; a fleet-aware override swaps in the worker-pool
    /// advertisement.
    /// </summary>
    /// <param name="request">The composed request.</param>
    /// <param name="bundle">The catalog bundle the class resolved to (non-null).</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    internal virtual Task<SlotAdmissionResult?> EvaluateCapacityAsync(
        SlotAdmissionRequest request,
        Environments.EnvironmentBundle bundle,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<SlotAdmissionResult?>(null);
    }

    /// <summary>
    /// Check (4) — isolation class the host can honor. Returns a deny
    /// <see cref="SlotAdmissionResult"/> when the host lacks the requested
    /// isolation (e.g. <c>strong</c> on a host without the strong driver);
    /// <c>null</c> when the check passes. Base returns <c>null</c>; the
    /// sandbox slice overrides it with the host capability map.
    /// </summary>
    /// <param name="request">The composed request.</param>
    /// <param name="bundle">The catalog bundle the class resolved to (non-null).</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    internal virtual Task<SlotAdmissionResult?> EvaluateIsolationAsync(
        SlotAdmissionRequest request,
        Environments.EnvironmentBundle bundle,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<SlotAdmissionResult?>(null);
    }

    /// <summary>
    /// Check (5) — edition coverage for the class/runtime. Returns a deny
    /// <see cref="SlotAdmissionResult"/> when the active edition cannot
    /// cover the request (e.g. Community + paid GPU class); <c>null</c>
    /// when the check passes. Base returns <c>null</c>; the editions
    /// registry override routes paid-feature decisions through the
    /// existing <c>EditionGate</c> function.
    /// </summary>
    /// <param name="request">The composed request.</param>
    /// <param name="bundle">The catalog bundle the class resolved to (non-null).</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    internal virtual Task<SlotAdmissionResult?> EvaluateEditionAsync(
        SlotAdmissionRequest request,
        Environments.EnvironmentBundle bundle,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<SlotAdmissionResult?>(null);
    }

    /// <summary>
    /// Check (6) — secret refs resolvable under the worker subject. Returns
    /// a deny <see cref="SlotAdmissionResult"/> when any ref is unknown;
    /// <c>null</c> when every ref is found. Base returns <c>null</c> only
    /// when the ref list is empty — a non-empty list of refs without a
    /// real catalog lookup must NOT silently pass today, so this method
    /// fails closed until the secret-catalog slice replaces it.
    /// </summary>
    /// <param name="request">The composed request.</param>
    /// <param name="bundle">The catalog bundle the class resolved to (non-null).</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    internal virtual Task<SlotAdmissionResult?> EvaluateSecretsAsync(
        SlotAdmissionRequest request,
        Environments.EnvironmentBundle bundle,
        CancellationToken cancellationToken)
    {
        return request.SecretRefs.Count > 0
            ? Task.FromResult<SlotAdmissionResult?>(Deny(AdmissionCodes.Secrets))
            : Task.FromResult<SlotAdmissionResult?>(null);
    }

    private static SlotAdmissionResult Admit()
    {
        return new SlotAdmissionResult(Admitted: true, AdmissionId: Guid.CreateVersion7(), DenialCode: null);
    }

    private static SlotAdmissionResult Deny(string code)
    {
        return new SlotAdmissionResult(Admitted: false, AdmissionId: null, DenialCode: code);
    }
}
