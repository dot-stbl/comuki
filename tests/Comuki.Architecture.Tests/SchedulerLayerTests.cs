using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Scheduler module (comuki-project-structure.md §2):
/// Domain is the innermost layer, Application sits on it through ports,
/// Infrastructure wires EF and the dispatcher hosted service, and no
/// module reaches into the engine or the hosts.
/// </summary>
public sealed class SchedulerLayerTests
{
    private const string SchedulerApplication = "Comuki.Modules.Scheduler.Application";
    private const string SchedulerInfrastructure = "Comuki.Modules.Scheduler.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void SchedulerDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Scheduler.Domain.Jobs.ScheduledJob).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                SchedulerApplication,
                SchedulerInfrastructure,
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
    public void SchedulerApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Scheduler.Application.Jobs.ScheduledJobService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(SchedulerInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void SchedulerInfrastructureMustNotDependOnEngineOrHosts()
    {
        // The Migrator is a composition host and may reference the module;
        // the module must never reference back.
        var result = Types
            .InAssembly(typeof(Modules.Scheduler.Infrastructure.Persistence.SchedulerDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
