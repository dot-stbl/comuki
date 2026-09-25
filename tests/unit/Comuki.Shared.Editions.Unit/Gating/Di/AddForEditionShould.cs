using Comuki.Shared.Editions.Edition;
using Comuki.Shared.Editions.Gating;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Options;
using Comuki.Shared.Editions.Unit.Fixtures;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Gating.Di;

/// <summary>
/// <c>AddForEdition&lt;TService&gt;</c> tests — per-service edition gating
/// at the DI-registration boundary. Exercises every documented branch:
/// the Community resolution path, the paid resolution path, the
/// type-assignability precondition failure (with no registration left
/// behind when it trips), the default-Singleton lifetime, and the
/// end-to-end path with a real <see cref="LicenseEdition"/> built from
/// the <see cref="TestLicense"/>
/// fixtures (Community vs Team-with-AgentEval).
/// </summary>
public sealed class AddForEditionShould
{
    [Fact(DisplayName = "Given a Community edition that does not cover the paid feature, when AddForEdition runs, then resolving TService returns the otherwise implementation")]
    public void CommunityEditionRegistersOtherwiseImplementation()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(false);
        var services = new ServiceCollection();
        services.AddSingleton(edition);

        services.AddForEdition<IForEditionService>(
            paid: Features.AgentEval,
            use: typeof(PaidForEditionService),
            otherwise: typeof(CommunityForEditionService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IForEditionService>()
            .ShouldBeOfType<CommunityForEditionService>();
    }

    [Fact(DisplayName = "Given a paid edition that covers the paid feature, when AddForEdition runs, then resolving TService returns the paid implementation")]
    public void PaidEditionRegistersUseImplementation()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(true);
        var services = new ServiceCollection();
        services.AddSingleton(edition);

        services.AddForEdition<IForEditionService>(
            paid: Features.AgentEval,
            use: typeof(PaidForEditionService),
            otherwise: typeof(CommunityForEditionService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IForEditionService>()
            .ShouldBeOfType<PaidForEditionService>();
    }

    [Fact(DisplayName = "Given a use type that is not assignable to TService, when AddForEdition runs, then it throws ArgumentException naming the use parameter, and no service is registered")]
    public void UseTypeNotAssignableThrowsAndRegistersNothing()
    {
        var edition = Substitute.For<IEdition>();
        var services = new ServiceCollection();
        services.AddSingleton(edition);
        var initialCount = services.Count;

        var exception = Should.Throw<ArgumentException>(() =>
            services.AddForEdition<IForEditionService>(
                paid: Features.AgentEval,
                use: typeof(UnrelatedService),
                otherwise: typeof(CommunityForEditionService)));

        exception.ParamName.ShouldBe("use");
        services.Count.ShouldBe(initialCount);

        using var provider = services.BuildServiceProvider();
        Should.Throw<InvalidOperationException>(provider.GetRequiredService<IForEditionService>);
    }

    [Fact(DisplayName = "Given an otherwise type that is not assignable to TService, when AddForEdition runs, then it throws ArgumentException naming the otherwise parameter, and no service is registered")]
    public void OtherwiseTypeNotAssignableThrowsAndRegistersNothing()
    {
        var edition = Substitute.For<IEdition>();
        var services = new ServiceCollection();
        services.AddSingleton(edition);
        var initialCount = services.Count;

        var exception = Should.Throw<ArgumentException>(() =>
            services.AddForEdition<IForEditionService>(
                paid: Features.AgentEval,
                use: typeof(PaidForEditionService),
                otherwise: typeof(UnrelatedService)));

        exception.ParamName.ShouldBe("otherwise");
        services.Count.ShouldBe(initialCount);

        using var provider = services.BuildServiceProvider();
        Should.Throw<InvalidOperationException>(provider.GetRequiredService<IForEditionService>);
    }

    [Fact(DisplayName = "Given the default lifetime, when AddForEdition registers and TService is resolved twice, then the same instance is returned")]
    public void DefaultLifetimeIsSingleton()
    {
        var edition = Substitute.For<IEdition>();
        edition.Has(Arg.Any<Catalog.Feature>()).Returns(true);
        var services = new ServiceCollection();
        services.AddSingleton(edition);

        services.AddForEdition<IForEditionService>(
            paid: Features.AgentEval,
            use: typeof(PaidForEditionService),
            otherwise: typeof(CommunityForEditionService));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IForEditionService>();
        var second = provider.GetRequiredService<IForEditionService>();

        first.ShouldBeSameAs(second);
    }

    [Fact(DisplayName = "Given a Community license token, when AddForEdition runs for an AgentEval-gated swap, then resolving TService returns the Community implementation")]
    public void CommunityTokenRegistersCommunityImplementation()
    {
        var services = NewServicesBackedByTestLicense(TestLicense.Community);

        services.AddForEdition<IForEditionService>(
            paid: Features.AgentEval,
            use: typeof(PaidForEditionService),
            otherwise: typeof(CommunityForEditionService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IForEditionService>()
            .ShouldBeOfType<CommunityForEditionService>();
    }

    [Fact(DisplayName = "Given a Team license token granting AgentEval, when AddForEdition runs for an AgentEval-gated swap, then resolving TService returns the paid implementation")]
    public void PaidTokenRegistersPaidImplementation()
    {
        var services = NewServicesBackedByTestLicense(TestLicense.With(Features.AgentEval));

        services.AddForEdition<IForEditionService>(
            paid: Features.AgentEval,
            use: typeof(PaidForEditionService),
            otherwise: typeof(CommunityForEditionService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IForEditionService>()
            .ShouldBeOfType<PaidForEditionService>();
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
        return services;
    }

    private interface IForEditionService;

    private sealed class PaidForEditionService : IForEditionService;

    private sealed class CommunityForEditionService : IForEditionService;

    private sealed class UnrelatedService;

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
