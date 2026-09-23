using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Verify module (comuki-project-structure.md §2):
/// Domain is the innermost layer, Application sits on it through ports,
/// Infrastructure wires EF and the verifier worker, and no module
/// reaches into the engine or the hosts.
/// </summary>
public sealed class VerifyLayerTests
{
    private const string VerifyApplication = "Comuki.Modules.Verify.Application";
    private const string VerifyInfrastructure = "Comuki.Modules.Verify.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void VerifyDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Verify.Domain.Runs.GenericCommandRun).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                VerifyApplication,
                VerifyInfrastructure,
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
    public void VerifyApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Verify.Application.Ports.IGenericCommandRunner).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(VerifyInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void VerifyInfrastructureMustNotDependOnEngineOrHosts()
    {
        // The Migrator is a composition host and may reference the module;
        // the module must never reference back.
        var result = Types
            .InAssembly(typeof(Modules.Verify.Infrastructure.Persistence.VerifyDbContext).Assembly)
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
