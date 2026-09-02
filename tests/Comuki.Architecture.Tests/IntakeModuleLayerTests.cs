using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Intake module (S6): Domain is innermost,
/// Application sits on it through ports, and no module layer reaches
/// into the engine or the hosts — the run launcher and run status
/// reader are host-composed ports by design.
/// </summary>
public sealed class IntakeModuleLayerTests
{
    private const string IntakeApplication = "Comuki.Modules.Intake.Application";
    private const string IntakeInfrastructure = "Comuki.Modules.Intake.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void IntakeDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Intake.Domain.Tickets.TicketProvider).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                IntakeApplication,
                IntakeInfrastructure,
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
    public void IntakeApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Intake.Application.Ports.Tickets.IIntakeStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(IntakeInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void IntakeModuleMustNotDependOnEngine()
    {
        // modules ↛ engine — the engine reaches modules through
        // contracts, never the reverse; intake creates runs only
        // through the host-composed IRunLauncher port.
        var domain = Types
            .InAssembly(typeof(Modules.Intake.Domain.Tickets.TicketProvider).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var application = Types
            .InAssembly(typeof(Modules.Intake.Application.Ports.Tickets.IIntakeStore).Assembly)
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
