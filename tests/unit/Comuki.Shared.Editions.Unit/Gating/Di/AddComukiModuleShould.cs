using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Catalog;
using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Licensing;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Registry;
using Comuki.Shared.Editions.Unit.Fixtures;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating.Di;

/// <summary>
/// <c>AddComukiModule&lt;TModule&gt;</c> tests — module-level edition
/// gating at the DI-registration boundary. Exercises every documented
/// branch: the ungated short-circuit (no bootstrap resolution at all),
/// the covered-feature path (installer runs), the not-covered path
/// (installer skipped + warning logged), the missing-dependency
/// precondition failure, and the end-to-end path with a real
/// <see cref="LicenseEdition"/> built from the
/// <see cref="Comuki.Shared.Editions.Unit.Fixtures.TestLicense"/>
/// fixtures (Community vs Team-with-MultiRepo).
/// </summary>
public sealed class AddComukiModuleShould
{
    private const string GatingLoggerCategory = "Comuki.Shared.Editions.Gating";

    [Fact(DisplayName = "Given a marker with no [EditionFeature], when AddComukiModule runs, then it calls the installer and never builds a bootstrap provider")]
    public void UngatedMarkerSkipsBootstrapAndRunsInstaller()
    {
        var services = new ServiceCollection();
        var installerCalled = false;

        services.AddComukiModule<UngatedMarker>(inner =>
        {
            installerCalled = true;
            inner.AddSingleton<IUngatedService, UngatedService>();
        });

        installerCalled.ShouldBeTrue();
        // The bootstrap resolution path would have thrown on a service
        // collection that registered nothing else, so reaching here at
        // all proves the ungated short-circuit skipped it. We still
        // resolve the installer-registered service through a real
        // provider to lock the registration in.
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUngatedService>().ShouldBeOfType<UngatedService>();
    }

    [Fact(DisplayName = "Given a marker with [EditionFeature] and a substituted IEdition that covers the feature, when AddComukiModule runs, then it calls the installer")]
    public void CoveredFeatureRunsInstaller()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Feature>()).Returns(true);
        var services = new ServiceCollection();
        services.AddSingleton(edition);
        services.AddSingleton<IEditionCapabilityRegistry, EditionCapabilityRegistry>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactoryInstance());
        var installerCalled = false;

        services.AddComukiModule<GatedMarker>(inner =>
        {
            installerCalled = true;
            inner.AddSingleton<IGatedService, GatedService>();
        });

        installerCalled.ShouldBeTrue();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGatedService>().ShouldBeOfType<GatedService>();
    }

    [Fact(DisplayName = "Given a marker with [EditionFeature] and an IEdition that does not cover the feature, when AddComukiModule runs, then the installer is skipped and a warning is logged")]
    public void NotCoveredFeatureSkipsInstallerAndLogsWarning()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Feature>()).Returns(false);
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var logger = Substitute.For<ILogger>();
        loggerFactory.CreateLogger(GatingLoggerCategory).Returns(logger);
        var services = new ServiceCollection();
        services.AddSingleton(edition);
        services.AddSingleton<IEditionCapabilityRegistry, EditionCapabilityRegistry>();
        services.AddSingleton(loggerFactory);
        var installerCalled = false;

        services.AddComukiModule<GatedMarker>(inner =>
        {
            installerCalled = true;
            inner.AddSingleton<IGatedService, GatedService>();
        });

        installerCalled.ShouldBeFalse();
        // NSubstitute's generic Log<TState> call is invoked here with
        // TState = the internal FormattedLogValues struct, not `object` —
        // a call-spec built with Arg.Is<object>(...) records a distinct
        // closed-generic Log<Object> expectation that never matches the
        // real Log<FormattedLogValues> invocation. Inspecting the recorded
        // call's arguments directly sidesteps that generic-matching pitfall.
        var call = logger.ReceivedCalls().ShouldHaveSingleItem();
        call.GetMethodInfo().Name.ShouldBe(nameof(ILogger.Log));
        var arguments = call.GetArguments();
        arguments[0].ShouldBe(LogLevel.Warning);
        var state = arguments[2]!.ToString().ShouldNotBeNull();
        state.ShouldContain("GatedMarker");
        state.ShouldContain("multi-repo");
    }

    [Fact(DisplayName = "Given a marker with [EditionFeature] but no IEdition registered, when AddComukiModule runs, then it throws at composition time")]
    public void MissingIEditionThrowsAtComposition()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEditionCapabilityRegistry, EditionCapabilityRegistry>();

        Should.Throw<InvalidOperationException>(() =>
            services.AddComukiModule<GatedMarker>(inner => inner.AddSingleton<IGatedService, GatedService>()));
    }

    [Fact(DisplayName = "Given a Community license token, when AddComukiModule runs for a multi-repo-gated marker, then the installer is skipped")]
    public void CommunityTokenSkipsGatedInstaller()
    {
        var services = NewServicesBackedByTestLicense(TestLicense.Community);

        var installerCalled = false;
        services.AddComukiModule<GatedMarker>(inner =>
        {
            installerCalled = true;
            inner.AddSingleton<IGatedService, GatedService>();
        });

        installerCalled.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a Team license token granting multi-repo, when AddComukiModule runs for a multi-repo-gated marker, then the installer runs")]
    public void PaidTokenRunsGatedInstaller()
    {
        var services = NewServicesBackedByTestLicense(TestLicense.With(Features.MultiRepo));

        var installerCalled = false;
        services.AddComukiModule<GatedMarker>(inner =>
        {
            installerCalled = true;
            inner.AddSingleton<IGatedService, GatedService>();
        });

        installerCalled.ShouldBeTrue();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGatedService>().ShouldBeOfType<GatedService>();
    }

    private static IServiceCollection NewServicesBackedByTestLicense(string token)
    {
        var clock = new MutableFakeTimeProvider(DateTimeOffset.UtcNow);
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(token));

        var options = new LicenseOptions
        {
            Path = "env:LICENSE",
            ReloadDelay = TimeSpan.FromMilliseconds(1),
        };
        var edition = new LicenseEdition(
            new StaticOptionsMonitor<LicenseOptions>(options),
            resolver,
            new Ed25519LicenseProvider(TestLicense.PublicKey, clock),
            clock,
            NullLogger<LicenseEdition>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton<IEdition>(edition);
        services.AddSingleton<IEditionCapabilityRegistry, EditionCapabilityRegistry>();
        return services;
    }

    private static ILoggerFactory NullLoggerFactoryInstance()
    {
        return NullLoggerFactory.Instance;
    }

    /// <summary>Marker carrying the gating attribute — used by every test that exercises the gated path.</summary>
    // NOTE: nameof(Features.MultiRepo) would yield "MultiRepo" (the C#
    // member name), not "multi-repo" (the catalog's well-formed dash-case
    // key) — Features/Limits field names are PascalCase while their keys
    // are dash-case, so the OQ1 fallback must use the literal key string,
    // never nameof, at any real call site.
    [EditionFeature("multi-repo")]
    private sealed class GatedMarker;

    /// <summary>Marker without the attribute — exercises the ungated short-circuit.</summary>
    private sealed class UngatedMarker;

    private interface IUngatedService;

    private sealed class UngatedService : IUngatedService;

    private interface IGatedService;

    private sealed class GatedService : IGatedService;

    /// <summary>Mutable fake clock — distinct from the one in <c>LicenseEditionShould</c> so the test file is self-contained.</summary>
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
    /// holds a single value, never fires <c>OnChange</c>. Mirrors
    /// <c>LicenseEditionShould.StaticOptionsMonitor</c>; duplicated here
    /// (per the brief) so test files do not share helpers across files.
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
}
