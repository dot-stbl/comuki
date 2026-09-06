using NetArchTest.Rules;
using Xunit;

namespace Comuki.Modules.Proxy.DomainLayerTests;

/// <summary>
/// Cross-module isolation assertions for the Proxy module — a follow-up
/// to the 2026-09-04 audit §4 finding (Proxy → Costs edge). Before
/// this commit the Proxy module referenced <c>Costs.Application</c>
/// for <c>IUsageEventStore</c> and <c>IUsageRecorder</c> ran through
/// Contracts. The carve-out promotes both to <c>Comuki.Shared.Contracts.Usage</c>
/// and removes the Proxy → Costs.Application ProjectReference; the
/// Proxy.DomainLayerTests project enforces that the edge stays broken.
/// </summary>
public sealed class ProxyDomainLayerTests
{
    private const string CostsApplication = "Comuki.Modules.Costs.Application";
    private const string CostsInfrastructure = "Comuki.Modules.Costs.Infrastructure";

    [Fact]
    public void ProxyApplicationMustNotDependOnCostsApplication()
    {
        // The Proxy module's metering + budgeting used to reach into the
        // Costs module's Application assembly for IUsageEventStore +
        // IUsageRecorder. The carve-out moves both surfaces to
        // Shared.Contracts.Usage; the edge into Costs.Application is gone.
        var result = Types
            .InAssembly(typeof(Application.Budgeting.DefaultProxyBudgetEnforcer).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(CostsApplication, CostsInfrastructure)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void ProxyInfrastructureMustNotDependOnCostsApplication()
    {
        // Proxy.Infrastructure (YARP transforms + auth handler + options)
        // never had a Costs edge to begin with — guard against future
        // drift. Module ↛ module is the project-structure rule.
        var result = Types
            .InAssembly(typeof(Infrastructure.Yarp.ProxyTransforms).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(CostsApplication, CostsInfrastructure)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
