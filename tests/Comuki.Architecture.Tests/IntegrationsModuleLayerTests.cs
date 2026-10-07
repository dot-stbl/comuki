using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Integrations module (issue #6, renamed from
/// Intake per the hard-rename change): Domain is innermost,
/// Application sits on it through ports, and no module layer reaches
/// into the engine or the hosts — the run launcher and run status
/// reader are host-composed ports by design.
/// </summary>
public sealed class IntegrationsModuleLayerTests
{
    private const string IntegrationsApplication = "Comuki.Modules.Integrations.Application";
    private const string IntegrationsInfrastructure = "Comuki.Modules.Integrations.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void IntegrationsDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Integrations.Domain.Items.TicketProvider).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                IntegrationsApplication,
                IntegrationsInfrastructure,
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
    public void IntegrationsApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Integrations.Application.Ports.Tickets.IIntegrationsStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(IntegrationsInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void IntegrationsModuleMustNotDependOnEngine()
    {
        // modules ↛ engine — the engine reaches modules through
        // contracts, never the reverse; integrations creates runs only
        // through the host-composed IRunLauncher port.
        var domain = Types
            .InAssembly(typeof(Modules.Integrations.Domain.Items.TicketProvider).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var application = Types
            .InAssembly(typeof(Modules.Integrations.Application.Ports.Tickets.IIntegrationsStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();

        Assert.True(domain.IsSuccessful, Failing(domain));
        Assert.True(application.IsSuccessful, Failing(application));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
