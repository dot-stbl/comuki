using Comuki.Shared.Bootstrap.Config;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Config;

/// <summary>
/// COMUKI_ENV resolution (issue #54): primary variable wins, the
/// ASPNETCORE_/DOTNET_ fallbacks apply quietly, blank values fall
/// through, nothing set resolves to Production.
/// </summary>
public sealed class ComukiEnvironmentShould
{
    [Fact(DisplayName = "Given COMUKI_ENV and both fallbacks set, when Resolve runs, then COMUKI_ENV wins")]
    public void PrimaryVariableWins()
    {
        var resolved = ComukiEnvironment.Resolve(static name => name switch
        {
            ComukiEnvironment.EnvironmentVariable => "development",
            ComukiEnvironment.AspNetCoreFallbackVariable => "Production",
            ComukiEnvironment.DotnetFallbackVariable => "Production",
            _ => null,
        });

        resolved.ShouldBe("development");
    }

    [Fact(DisplayName = "Given only ASPNETCORE_ENVIRONMENT set, when Resolve runs, then it is used")]
    public void AspNetCoreFallbackApplies()
    {
        var resolved = ComukiEnvironment.Resolve(static name => name switch
        {
            ComukiEnvironment.EnvironmentVariable => null,
            ComukiEnvironment.AspNetCoreFallbackVariable => "Development",
            _ => null,
        });

        resolved.ShouldBe("Development");
    }

    [Fact(DisplayName = "Given only DOTNET_ENVIRONMENT set, when Resolve runs, then it is used")]
    public void DotnetFallbackApplies()
    {
        var resolved = ComukiEnvironment.Resolve(static name => name switch
        {
            ComukiEnvironment.EnvironmentVariable => null,
            ComukiEnvironment.AspNetCoreFallbackVariable => null,
            ComukiEnvironment.DotnetFallbackVariable => "Staging",
            _ => null,
        });

        resolved.ShouldBe("Staging");
    }

    [Fact(DisplayName = "Given a whitespace COMUKI_ENV, when Resolve runs, then it is skipped for the fallbacks")]
    public void BlankPrimaryFallsThrough()
    {
        var resolved = ComukiEnvironment.Resolve(static name => name switch
        {
            ComukiEnvironment.EnvironmentVariable => "   ",
            ComukiEnvironment.AspNetCoreFallbackVariable => null,
            ComukiEnvironment.DotnetFallbackVariable => "production",
            _ => null,
        });

        resolved.ShouldBe("production");
    }

    [Fact(DisplayName = "Given nothing is set, when Resolve runs, then Production is the default")]
    public void NothingSetDefaultsToProduction()
    {
        var resolved = ComukiEnvironment.Resolve(static _ => null);

        resolved.ShouldBe(ComukiEnvironment.ProductionName);
    }
}
