using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Licensing.Status;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Tiers;
using Comuki.Shared.Editions.Unit.Fixtures;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Edition;

/// <summary>
/// <see cref="LicenseEdition"/> tests — the hot-reloading
/// <see cref="IEdition"/> port. Exercises every observable state
/// (Absent / Valid / Grace / Expired), every failure branch (resolver
/// unset, bad signature, missing file), the rank-vs-allowlist dispatch
/// in <see cref="LicenseEdition.Has"/>, the license-override path in
/// <see cref="LicenseEdition.Limit"/>, and the hot-reload path
/// end-to-end with a real <see cref="FileSecretProvider"/> pointed at
/// a temp file the test overwrites between reads.
/// </summary>
public sealed class LicenseEditionShould
{
    private static readonly DateTimeOffset startNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset farFuture = new(2099, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static IOptionsMonitor<LicenseOptions> MonitorFor(LicenseOptions options)
    {
        return new StaticOptionsMonitor<LicenseOptions>(options);
    }

    private static LicenseEdition BuildEdition(
        LicenseOptions options,
        ISecretResolver resolver,
        ILicenseProvider provider,
        TimeProvider clock)
    {
        return new LicenseEdition(MonitorFor(options), resolver, provider, clock, NullLogger<LicenseEdition>.Instance);
    }

    private static string Sign(LicenseGrant grant, byte[] privateSeed)
    {
        return Ed25519LicenseSigner.Sign(grant, privateSeed);
    }

    [Fact(DisplayName = "Given no Path configured, when edition members are read, then Current is Community, Status is Absent, Has(MultiRepo) is false, and Limit(Projects) is 1")]
    public void NoPathIsCommunityAbsent()
    {
        var (publicKey, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);

        var resolver = Substitute.For<ISecretResolver>();
        var edition = BuildEdition(new LicenseOptions(), resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Current.ShouldBe(EditionTier.Community);
        edition.Status.ShouldBe(LicenseStatus.Absent);
        edition.IsDegraded.ShouldBeFalse();
        edition.Has(Features.MultiRepo).ShouldBeFalse();
        edition.Limit(Limits.Projects).ShouldBe(1);
    }

    [Fact(DisplayName = "Given a valid Team license in ImplicitByRank mode, when edition is read, then Current is Team, Status is Valid, and Has(MultiRepo) is true by rank")]
    public void TeamLicenseImplicitByRankCoversRankedFeature()
    {
        var (publicKey, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);
        var token = Sign(new LicenseGrant(
            Org: "Acme Inc",
            Tier: EditionTiers.Team,
            Expiry: farFuture,
            Mode: LicenseMode.ImplicitByRank,
            Features: ["multi-repo"]), privateSeed);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Current.ShouldBe(EditionTiers.Team);
        edition.Status.ShouldBe(LicenseStatus.Valid);
        edition.IsDegraded.ShouldBeFalse();
        edition.Has(Features.MultiRepo).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a Team license in ExplicitAllowlist mode with Features=[], when Has(MultiRepo) is called, then it is false even though the rank would have covered it")]
    public void TeamLicenseExplicitAllowlistEmptyFeaturesDenies()
    {
        var (publicKey, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);
        var token = Sign(new LicenseGrant(
            Org: "Acme Inc",
            Tier: EditionTiers.Team,
            Expiry: farFuture,
            Mode: LicenseMode.ExplicitAllowlist,
            Features: []), privateSeed);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Has(Features.MultiRepo).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a token signed with keypair A but verified with keypair B's public key, when edition is read, then it falls back to Community/Absent")]
    public void BadSignatureFallsBackToCommunity()
    {
        var (_, privateSeedA) = Ed25519LicenseSigner.GenerateKeyPair();
        var (publicKeyB, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);

        var token = Sign(new LicenseGrant(
            Org: "Acme Inc",
            Tier: EditionTiers.Team,
            Expiry: farFuture,
            Mode: LicenseMode.ImplicitByRank), privateSeedA);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKeyB, clock), clock);

        edition.Current.ShouldBe(EditionTier.Community);
        edition.Status.ShouldBe(LicenseStatus.Absent);
    }

    [Fact(DisplayName = "Given the resolver throws SecretRefUnsetException, when edition is read, then it falls back to Community/Absent without throwing")]
    public void ResolverUnsetFallsBack()
    {
        var (publicKey, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);

        var resolver = Substitute.For<ISecretResolver>();
        resolver.When(x => x.ResolveAsync("env:MISSING", Arg.Any<CancellationToken>()))
            .Do(_ => throw new SecretRefUnsetException("env:MISSING"));

        var options = new LicenseOptions { Path = "env:MISSING", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Current.ShouldBe(EditionTier.Community);
        edition.Status.ShouldBe(LicenseStatus.Absent);
        Should.NotThrow(() => edition.Has(Features.MultiRepo));
        Should.NotThrow(() => edition.Limit(Limits.Projects));
    }

    [Fact(DisplayName = "Given a Team license with Limits override projects=25, when edition.Limit(Projects) is called, then 25 wins over the registry's Team default of 10")]
    public void LimitOverrideWinsOverRegistry()
    {
        var (publicKey, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var clock = new MutableFakeTimeProvider(startNow);
        var token = Sign(new LicenseGrant(
            Org: "Acme Inc",
            Tier: EditionTiers.Team,
            Expiry: farFuture,
            Mode: LicenseMode.ImplicitByRank,
            Limits: new Dictionary<string, int> { ["projects"] = 25 }), privateSeed);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Limit(Limits.Projects).ShouldBe(25);
    }

    [Fact(DisplayName = "Given a license expired past grace, when edition is read, then IsDegraded is true, Status is Expired, but Current still reports the licensed tier")]
    public void ExpiredPastGraceIsDegradedButKeepsTier()
    {
        var (publicKey, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var grace = TimeSpan.FromHours(1);
        var expiry = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var nowPastGrace = expiry + grace + TimeSpan.FromMinutes(1);
        var clock = new MutableFakeTimeProvider(nowPastGrace);

        var token = Sign(new LicenseGrant(
            Org: "Acme Inc",
            Tier: EditionTiers.Team,
            Expiry: expiry,
            Mode: LicenseMode.ImplicitByRank), privateSeed);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", GracePeriod = grace, ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

        edition.Status.ShouldBe(LicenseStatus.Expired);
        edition.IsDegraded.ShouldBeTrue();
        edition.Current.ShouldBe(EditionTiers.Team);
    }

    [Fact(DisplayName = "Given a real file-backed resolver pointed at a license file, when the file is overwritten and the throttle elapses, then Limit(Projects) reflects the new license")]
    public void HotReloadPicksUpReplacedLicense()
    {
        var (publicKey, privateSeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var tempFile = Path.GetTempFileName();
        try
        {
            var licenseA = Sign(new LicenseGrant(
                Org: "Acme Inc",
                Tier: EditionTiers.Team,
                Expiry: farFuture,
                Mode: LicenseMode.ImplicitByRank,
                Limits: new Dictionary<string, int> { ["projects"] = 10 }), privateSeed);
            File.WriteAllText(tempFile, licenseA);

            var fileProvider = new FileSecretProvider(
                Microsoft.Extensions.Options.Options.Create(new FileSecretOptions { Enabled = true }));
            var resolver = new CompositeSecretResolver([fileProvider]);

            var clock = new MutableFakeTimeProvider(startNow);
            var options = new LicenseOptions
            {
                Path = $"file:{tempFile}",
                ReloadDelay = TimeSpan.FromSeconds(5),
                GracePeriod = TimeSpan.FromDays(7),
            };
            var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(publicKey, clock), clock);

            edition.Limit(Limits.Projects).ShouldBe(10);

            var licenseB = Sign(new LicenseGrant(
                Org: "Acme Inc",
                Tier: EditionTiers.Team,
                Expiry: farFuture,
                Mode: LicenseMode.ImplicitByRank,
                Limits: new Dictionary<string, int> { ["projects"] = 50 }), privateSeed);
            File.WriteAllText(tempFile, licenseB);

            clock.SetUtcNow(startNow + options.ReloadDelay + TimeSpan.FromSeconds(1));

            edition.Limit(Limits.Projects).ShouldBe(50);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>Mutable fake clock — distinct from the fixed-constructor one in <c>Ed25519LicenseProviderShould</c>.</summary>
    private sealed class MutableFakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            now = value;
        }
    }

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{T}"/> adapter for tests —
    /// holds a single value, never fires <c>OnChange</c>. Avoids the
    /// <c>ServiceCollection</c> + <c>Configure&lt;T&gt;</c> init-only-
    /// property dance for test options that have no live-reload
    /// semantics to exercise.
    /// </summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }

    [Fact(DisplayName = "Given a dev-overlay Team license and a provider constructed with both keys, when edition is read, then Current is Team")]
    public void DevLicenseResolvesToTeamTier()
    {
        var (prodPublic, _) = Ed25519LicenseSigner.GenerateKeyPair();
        var devPublic = TestLicense.DevPublicKey;
        var clock = new MutableFakeTimeProvider(startNow);
        var token = TestLicense.WithDev(EditionTiers.Team);

        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions { Path = "env:LICENSE", ReloadDelay = TimeSpan.FromMilliseconds(1) };
        var edition = BuildEdition(options, resolver, new Ed25519LicenseProvider(prodPublic, devPublic, clock), clock);

        edition.Current.ShouldBe(EditionTiers.Team);
        edition.Status.ShouldBe(LicenseStatus.Valid);
    }
}
