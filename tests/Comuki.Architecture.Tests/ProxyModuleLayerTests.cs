using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Proxy module (issue #8): Application sits on
/// Shared.Contracts through <c>IUsageRecorder</c> + <c>IUsageEventStore</c>;
/// Infrastructure wires the YARP pipeline + the auth handler. Neither
/// layer reaches into the engine, the hosts, or any sibling module
/// — proxy metering is a host-composed Contracts surface (the budget
/// gate is wired in <see cref="Host.HostComposer.Compose"/>, the
/// single composition method production and integration tests both boot
/// from).
/// </summary>
public sealed class ProxyModuleLayerTests
{
    private const string ProxyInfrastructure = "Comuki.Modules.Proxy.Infrastructure";
    private const string CostsApplication = "Comuki.Modules.Costs.Application";
    private const string CostsInfrastructure = "Comuki.Modules.Costs.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void ProxyApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Proxy.Application.Budgeting.DefaultProxyBudgetEnforcer).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ProxyInfrastructure,
                CostsApplication,
                CostsInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProxyInfrastructureMustNotDependOnEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Proxy.Infrastructure.Yarp.ProxyTransforms).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                CostsApplication,
                CostsInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
