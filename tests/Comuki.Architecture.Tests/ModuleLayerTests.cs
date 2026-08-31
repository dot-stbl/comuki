using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the first module (comuki-project-structure.md §2):
/// Domain is the innermost layer, Application sits on it through ports,
/// Infrastructure wires EF and the auth plumbing, and no module reaches
/// into the engine or the hosts.
/// </summary>
public sealed class ModuleLayerTests
{
    private const string IdentityApplication = "Comuki.Modules.Identity.Application";
    private const string IdentityInfrastructure = "Comuki.Modules.Identity.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";

    [Fact]
    public void IdentityDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Identity.Domain.Roles.Role).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                IdentityApplication,
                IdentityInfrastructure,
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
    public void IdentityApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Identity.Application.Authorization.IPermissionEvaluator).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(IdentityInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void IdentityInfrastructureMustNotDependOnEngineOrHosts()
    {
        // The Migrator is a composition host and may reference the module;
        // the module must never reference back.
        var result = Types
            .InAssembly(typeof(Modules.Identity.Infrastructure.Persistence.IdentityDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void IdentityModuleMustNotDependOnEngine()
    {
        // modules ↛ engine (structure §2 "Правила зависимостей") — the
        // engine reaches modules through contracts, never the reverse.
        var domain = Types
            .InAssembly(typeof(Modules.Identity.Domain.Roles.Role).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var application = Types
            .InAssembly(typeof(Modules.Identity.Application.Authorization.IPermissionEvaluator).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Engine, EngineCompute)
            .GetResult();
        var infrastructure = Types
            .InAssembly(typeof(Modules.Identity.Infrastructure.Persistence.IdentityDbContext).Assembly)
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
