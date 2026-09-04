using Comuki.Host.Security.RateLimit;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.RateLimit;

/// <summary>
/// Unit tests for <see cref="RateLimitInstaller"/>: option binding defaults,
/// the option-name constants, and that AddRateLimiter was wired without
/// throwing on the four named partitions.
/// </summary>
public sealed class RateLimitInstallerShould
{
    [Fact(DisplayName = "Given default config, when AddComukiRateLimit is called, then the installer registers without throwing")]
    public void RegisterNamedPolicies()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        Should.NotThrow(() => RateLimitInstaller.AddComukiRateLimit(services, config));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        // The framework exposes one composite options bag with the named
        // partition registry; the policy names surface through .AddPolicy().
        options.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given zero permits for one partition, when installed, then registration succeeds (that partition becomes a no-op)")]
    public void DisablePartitionWithZeroPermits()
    {
        var services = new ServiceCollection();
        var config = BuildConfiguration(("Host:RateLimit:LoginPermitsPerMinute", "0"));

        Should.NotThrow(() => RateLimitInstaller.AddComukiRateLimit(services, config));
    }

    [Fact(DisplayName = "Given RateLimitOptions, when bound from config, then every field round-trips")]
    public void BindAllFields()
    {
        var config = BuildConfiguration(
            ("Host:RateLimit:LoginPermitsPerMinute", "5"),
            ("Host:RateLimit:OidcStartPermitsPerMinute", "12"),
            ("Host:RateLimit:RunDecisionPermitsPerMinute", "30"),
            ("Host:RateLimit:ApiPermitsPerMinute", "1000"));

        var bound = config.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        bound.LoginPermitsPerMinute.ShouldBe(5);
        bound.OidcStartPermitsPerMinute.ShouldBe(12);
        bound.RunDecisionPermitsPerMinute.ShouldBe(30);
        bound.ApiPermitsPerMinute.ShouldBe(1000);
    }

    [Fact(DisplayName = "Given default options, when bound from empty config, then defaults carry")]
    public void ApplyDefaults()
    {
        var config = new ConfigurationBuilder().Build();

        var bound = config.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        bound.LoginPermitsPerMinute.ShouldBe(10);
        bound.OidcStartPermitsPerMinute.ShouldBe(30);
        bound.RunDecisionPermitsPerMinute.ShouldBe(60);
        bound.ApiPermitsPerMinute.ShouldBe(600);
    }

    [Fact(DisplayName = "Named partition constants are stable strings the controllers reference")]
    public void NamedPartitionsAreStable()
    {
        RateLimitPolicies.Login.ShouldBe("comuki.ratelimit.login");
        RateLimitPolicies.OidcStart.ShouldBe("comuki.ratelimit.oidc-start");
        RateLimitPolicies.RunDecision.ShouldBe("comuki.ratelimit.run-decision");
        RateLimitPolicies.Api.ShouldBe("comuki.ratelimit.api");
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(static p => p.Key, static p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }
}
