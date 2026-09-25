using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Tiers;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Shared.Editions.Edition;

/// <summary>
/// Hot-reloading implementation of <see cref="IEdition"/>: re-verifies
/// the underlying license on every public read but throttled to at
/// most once per <see cref="LicenseOptions.ReloadDelay"/>. Concurrent
/// callers are double-gated with <see cref="refreshLock"/> so a burst
/// of reads after a long quiet period does not stampede the resolver
/// and the crypto verifier.
/// <para>
/// Resolve failures (missing file, malformed reference, bad signature)
/// are logged at <c>Warning</c> and degrade to
/// <see cref="LicenseStatus.Absent"/> /
/// <see cref="EditionTier.Community"/> — they are not boot failures.
/// Boot-time shape checks are <see cref="LicenseOptionsValidator"/>'s
/// job; this type's job is graceful runtime degradation when the
/// operator replaces a license, lets it expire, or has a typo in the
/// path that the boot-time check missed.
/// </para>
/// </summary>
/// <remarks>DI constructor in dependencies-first order matching the rest of this repo's DI classes.</remarks>
public sealed class LicenseEdition(
    IOptionsMonitor<LicenseOptions> optionsMonitor,
    ISecretResolver secretResolver,
    ILicenseProvider licenseProvider,
    TimeProvider clock,
    ILogger<LicenseEdition> logger) : IEdition
{
    private readonly IOptionsMonitor<LicenseOptions> optionsMonitor = optionsMonitor;
    private readonly ISecretResolver secretResolver = secretResolver;
    private readonly ILicenseProvider licenseProvider = licenseProvider;
    private readonly TimeProvider clock = clock;
    private readonly ILogger<LicenseEdition> logger = logger;

    private volatile Snapshot snapshot = Snapshot.ForceRefresh();
    private readonly Lock refreshLock = new();

    /// <inheritdoc />
    public EditionTier Current => EnsureFresh().Current;

    /// <inheritdoc />
    public LicenseStatus Status => EnsureFresh().Status;

    /// <inheritdoc />
    public bool IsDegraded => Status == LicenseStatus.Expired;

    /// <inheritdoc />
    public bool Has(Feature feature)
    {
        var current = EnsureFresh();
        return current.License is { Mode: var mode } license && mode == LicenseMode.ExplicitAllowlist
            ? license.Features.Contains(feature.Key.Value)
            : current.Current.Rank >= feature.MinimumRank;
    }

    /// <inheritdoc />
    public int Limit(Limit limit)
    {
        var current = EnsureFresh();
        return current.License is { } license && license.Limits.TryGetValue(limit.Key.Value, out var overrideValue)
            ? overrideValue
            : limit.ValueFor(current.Current);
    }

    /// <summary>
    /// Returns the current snapshot, recomputing only when the throttle
    /// window has elapsed. Visible to the unit project via
    /// <c>InternalsVisibleTo("Comuki.Shared.Editions.Unit")</c> so the
    /// boundary cases (first read after construction; read after the
    /// configured reload delay elapses) can be observed precisely without
    /// racing through the public surface — most tests still go through
    /// the public members, this exists for the throttle boundary.
    /// </summary>
    internal Snapshot EnsureFresh()
    {
        var now = clock.GetUtcNow();
        var current = snapshot;
        if (now - current.CheckedAt < optionsMonitor.CurrentValue.ReloadDelay)
        {
            return current;
        }

        lock (refreshLock)
        {
            current = snapshot;
            if (now - current.CheckedAt < optionsMonitor.CurrentValue.ReloadDelay)
            {
                return current;
            }

            var refreshed = ComputeSnapshot(now);
            snapshot = refreshed;
            return refreshed;
        }
    }

    /// <summary>
    /// Resolves and verifies the configured license path at
    /// <paramref name="now"/>, classifying the outcome against the
    /// configured grace window. Visible to tests so the
    /// resolve/verify failure branches can be exercised directly.
    /// </summary>
    internal Snapshot ComputeSnapshot(DateTimeOffset now)
    {
        var path = optionsMonitor.CurrentValue.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Snapshot(LicenseStatus.Absent, EditionTier.Community, null, now);
        }

        LicenseKey? license;
        try
        {
            // intentional sync-over-async: IEdition has no async surface and
            // the consumer is a getter on every Has()/Limit() call. The same
            // shape lives in the validator path above. VSTHRD102 normally
            // reserves blocking calls for public entry points; ComputeSnapshot
            // is internal but IS the entry point for this exact concern (no
            // async caller exists anywhere above it in this type), so the
            // warning is suppressed rather than routed around.
#pragma warning disable VSTHRD002
#pragma warning disable VSTHRD102
            var resolved = secretResolver.ResolveAsync(path).GetAwaiter().GetResult();
#pragma warning restore VSTHRD102
#pragma warning restore VSTHRD002
            license = licenseProvider.Verify(resolved!);
        }
        catch (Exception exception) when (exception is SecretRefUnsetException or SecretRefFormatException or LicenseInvalidException)
        {
            logger.LogWarning(exception, "license at {Path} could not be verified; falling back to Community", path);
            return new Snapshot(LicenseStatus.Absent, EditionTier.Community, null, now);
        }

        var classified = LicenseEvaluator.Classify(license, now, optionsMonitor.CurrentValue.GracePeriod);
        return new Snapshot(classified.Status, classified.Current, license, now);
    }

    /// <summary>
    /// Immutable point-in-time view of the license resolution outcome.
    /// <see cref="CheckedAt"/> is the throttle anchor: the very first
    /// <see cref="EnsureFresh"/> after construction always recomputes
    /// because <see cref="ForceRefresh"/>'s sentinel <c>CheckedAt</c> is
    /// <see cref="DateTimeOffset.MinValue"/>. Nested private state-holder
    /// type on the class that owns it — the one allowed exception to
    /// "no private types in production code" (<c>code-shape.md</c> §1a).
    /// </summary>
    /// <param name="Status">Where the license sits in its lifecycle at <paramref name="CheckedAt"/>.</param>
    /// <param name="Current">The effective tier right now.</param>
    /// <param name="License">The verified license, or <c>null</c> when no license applies.</param>
    /// <param name="CheckedAt">When this snapshot was last computed; throttle anchor.</param>
    internal sealed record Snapshot(LicenseStatus Status, EditionTier Current, LicenseKey? License, DateTimeOffset CheckedAt)
    {
        /// <summary>An obviously-stale sentinel whose <see cref="CheckedAt"/> guarantees the first read always recomputes.</summary>
        public static Snapshot ForceRefresh()
        {
            return new(LicenseStatus.Absent, EditionTier.Community, null, DateTimeOffset.MinValue);
        }
    }
}
