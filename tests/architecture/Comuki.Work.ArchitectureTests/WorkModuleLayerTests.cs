using Comuki.Modules.Work.Infrastructure.Persistence;
using NetArchTest.Rules;
using Xunit;

namespace Comuki.Work.ArchitectureTests;

/// <summary>
/// Layer rules for the Work module (S6, add-work-management/specs/work-management/spec.md):
/// Domain is innermost, Application sits on it through ports, and no module layer
/// reaches into the engine or the hosts — the run launcher and run status reader
/// are host-composed ports by design. Mirrors
/// <c>Comuki.Architecture.Tests.IntegrationsModuleLayerTests</c> for the Work bounded
/// context that lands in three work-streams (domain / application / infrastructure)
/// per task 1.1 of the change. Tests assert the Work.Domain assembly does not
/// reach into outer layers, contract namespaces, or sibling modules.
/// </summary>
public sealed class WorkModuleLayerTests
{
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";
    private const string SharedContracts = "Comuki.Shared.Contracts";

    [Fact]
    public void WorkDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Work.Domain.WorkTask).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                SharedContracts)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void WorkDomainMustNotDependOnSiblingModuleDomains()
    {
        // Work domain must keep its own concepts; reaching into a
        // sibling module's Domain (e.g. Mission via add-minimal-missions)
        // is the boundary violation the change spec calls out — the
        // Mission id lives as a forward-declared type in Work.Domain
        // until the Missions module lands, after which the dependency
        // direction is Missions → Work only.
        var result = Types
            .InAssembly(typeof(Modules.Work.Domain.WorkTask).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Comuki.Modules.Identity.Domain",
                "Comuki.Modules.Costs.Domain",
                "Comuki.Modules.Integrations.Domain",
                "Comuki.Modules.Proxy.Domain",
                "Comuki.Modules.Chat.Domain",
                "Comuki.Modules.Knowledge.Domain",
                "Comuki.Modules.Memory.Domain",
                "Comuki.Modules.Projects.Domain",
                "Comuki.Modules.Scheduler.Domain",
                "Comuki.Modules.Artifacts.Domain",
                "Comuki.Modules.Repositories.Domain",
                "Comuki.Modules.Procedures.Domain",
                "Comuki.Modules.Verify.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void WorkDomainMustNotDependOnApplication()
    {
        // Domain must not reach into Application. Work.Application
        // (added in task 2.1) will reverse-reference Work.Domain only.
        // This test pre-empts the boundary violation before the
        // sibling project lands.
        var result = Types
            .InAssembly(typeof(Modules.Work.Domain.WorkTask).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Comuki.Modules.Work.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void WorkInfrastructureMustNotDependOnSiblingModulesOrEngine()
    {
        // The M2 layer-discipline invariant: Work.Infrastructure must
        // reference Work's own layers and the Shared.Contracts wire
        // surface only — never the engine, never the Integrations or
        // any other sibling module. Cross-module interaction lives
        // behind Application ports closed by Host adapters (see
        // HostInboundItemReader, HostIntegrationsSyncPort,
        // HostOrchestrationOutboxReader).
        var result = Types
            .InAssembly(typeof(WorkDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                Engine,
                EngineCompute,
                "Comuki.Modules.Identity.Application",
                "Comuki.Modules.Identity.Infrastructure",
                "Comuki.Modules.Costs.Application",
                "Comuki.Modules.Costs.Infrastructure",
                "Comuki.Modules.Integrations.Application",
                "Comuki.Modules.Integrations.Infrastructure",
                "Comuki.Modules.Proxy.Application",
                "Comuki.Modules.Proxy.Infrastructure",
                "Comuki.Modules.Chat.Application",
                "Comuki.Modules.Chat.Infrastructure",
                "Comuki.Modules.Knowledge.Application",
                "Comuki.Modules.Knowledge.Infrastructure",
                "Comuki.Modules.Memory.Application",
                "Comuki.Modules.Memory.Infrastructure",
                "Comuki.Modules.Projects.Application",
                "Comuki.Modules.Projects.Infrastructure",
                "Comuki.Modules.Scheduler.Application",
                "Comuki.Modules.Scheduler.Infrastructure",
                "Comuki.Modules.Artifacts.Application",
                "Comuki.Modules.Artifacts.Infrastructure",
                "Comuki.Modules.Repositories.Application",
                "Comuki.Modules.Repositories.Infrastructure",
                "Comuki.Modules.Procedures.Application",
                "Comuki.Modules.Procedures.Infrastructure",
                "Comuki.Modules.Verify.Application",
                "Comuki.Modules.Verify.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
