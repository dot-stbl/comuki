using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Cross-module invariants (comuki-project-structure.md §2):
/// Shared.Contracts is the only thing the engine / host / translator
/// import; modules may import Shared.Contracts and Shared.Kernel, and
/// nothing else from another module. Catching a leaked
/// <c>Comuki.Modules.X.Domain</c> reference inside Contracts (or inside a
/// sibling module's Application) is the point of this suite.
/// </summary>
public sealed class SharedContractsModuleBoundaryTests
{
    private const string IdentityDomain = "Comuki.Modules.Identity.Domain";
    private const string CostsDomain = "Comuki.Modules.Costs.Domain";
    private const string IntakeDomain = "Comuki.Modules.Intake.Domain";
    private const string ProxyDomain = "Comuki.Modules.Proxy.Domain";
    private const string ChatDomain = "Comuki.Modules.Chat.Domain";
    private const string KnowledgeDomain = "Comuki.Modules.Knowledge.Domain";
    private const string MemoryDomain = "Comuki.Modules.Memory.Domain";
    private const string ProjectsDomain = "Comuki.Modules.Projects.Domain";
    private const string SchedulerDomain = "Comuki.Modules.Scheduler.Domain";
    private const string ArtifactsDomain = "Comuki.Modules.Artifacts.Domain";

    [Fact]
    public void ContractsMustNotDependOnAnyModuleDomain()
    {
        var result = Types
            .InAssembly(typeof(Shared.Contracts.Usage.IUsageEventStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                IdentityDomain,
                CostsDomain,
                IntakeDomain,
                ProxyDomain,
                ChatDomain,
                KnowledgeDomain,
                MemoryDomain,
                ProjectsDomain,
                SchedulerDomain,
                ArtifactsDomain)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProxyApplicationMustNotDependOnCostsDomain()
    {
        // The proxy pre-flight used to reach into Costs.Domain for the
        // UsageSource enum (via IUsageEventStore / UsageEvent). After the
        // contracts cleanup the only allowed edge is Shared.Contracts
        // (UsageSources constants) — sibling-module Domain must stay out.
        var result = Types
            .InAssembly(typeof(Modules.Proxy.Application.Budgeting.DefaultProxyBudgetEnforcer).Assembly)
            .ShouldNot()
            .HaveDependencyOn(CostsDomain)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
