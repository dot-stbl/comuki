using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the shared/modules/engine/host layout
/// (comuki-project-structure.md §2): dependencies point inward,
/// shared is pure, hosts are composition-only.
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Contracts = "Comuki.Shared.Contracts";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";

    [Fact]
    public void KernelMustNotDependOnAnything()
    {
        var result = Types
            .InAssembly(typeof(Shared.Kernel.Ids.RunId).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Contracts, Engine, Host, Translator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ContractsMustDependOnlyOnKernel()
    {
        var result = Types
            .InAssembly(typeof(Shared.Contracts.Compute.IComputeProvider).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, Host, Translator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void EngineMustNotDependOnHosts()
    {
        var result = Types
            .InAssembly(typeof(Engine.Orchestration.Domain.RunStatus).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Host, Translator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void TranslatorMustNotDependOnEngine()
    {
        // Translator talks to the orchestrator over the wire (gRPC/HTTP) only.
        // Its own namespace starts with "Comuki.Host", so a dependency check
        // against the Host namespace would prefix-match itself — the real
        // boundary is the engine.
        var result = Types
            .InAssembly(typeof(Host.Translator.Parsing.StreamJsonParser).Assembly)
            .ShouldNot()
            .HaveDependencyOn(Engine)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
