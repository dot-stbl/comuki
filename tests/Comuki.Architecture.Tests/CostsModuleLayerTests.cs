using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Costs module (S9 T9.5): Domain innermost,
/// Application through ports, no reach into engine/hosts — budget gate
/// and project budget settings are host-composed contracts.
/// </summary>
public sealed class CostsModuleLayerTests
{
    private const string CostsApplication = "Comuki.Modules.Costs.Application";
    private const string CostsInfrastructure = "Comuki.Modules.Costs.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void CostsDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Costs.Domain.Events.UsageEvent).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                CostsApplication,
                CostsInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                "Comuki.Shared.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void CostsApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Costs.Application.Ports.IUsageEventStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(CostsInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void CostsModuleMustNotDependOnEngine()
    {
        var domain = Types
            .InAssembly(typeof(Modules.Costs.Domain.Events.UsageEvent).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var application = Types
            .InAssembly(typeof(Modules.Costs.Application.Ports.IUsageEventStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var infrastructure = Types
            .InAssembly(typeof(Modules.Costs.Infrastructure.Persistence.CostsDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();

        Assert.True(domain.IsSuccessful, Failing(domain));
        Assert.True(application.IsSuccessful, Failing(application));
        Assert.True(infrastructure.IsSuccessful, Failing(infrastructure));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
