using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Procedures module (comuki-project-structure.md §2):
/// Domain is the innermost layer (procedures catalog + editions gate
/// from tasks 1.1–1.3), Application sits on it through the compile-gate
/// and version-store ports, Infrastructure wires EF, and no module
/// reaches into the engine or the hosts. Sibling-module isolation
/// matches the spec — no reference to Identity, Projects,
/// Repositories, or Orchestration implementations from anywhere in
/// the Procedures module. The Migrator is a composition host and may
/// reference the module's DbContext to drive migrations; the module
/// must never reference back.
/// </summary>
public sealed class ProceduresLayerTests
{
    private const string ProceduresApplication = "Comuki.Modules.Procedures.Application";
    private const string ProceduresInfrastructure = "Comuki.Modules.Procedures.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";
    private const string IdentityDomain = "Comuki.Modules.Identity.Domain";
    private const string IdentityApplication = "Comuki.Modules.Identity.Application";
    private const string IdentityInfrastructure = "Comuki.Modules.Identity.Infrastructure";
    private const string ProjectsDomain = "Comuki.Modules.Projects.Domain";
    private const string ProjectsApplication = "Comuki.Modules.Projects.Application";
    private const string ProjectsInfrastructure = "Comuki.Modules.Projects.Infrastructure";
    private const string RepositoriesDomain = "Comuki.Modules.Repositories.Domain";
    private const string RepositoriesApplication = "Comuki.Modules.Repositories.Application";
    private const string RepositoriesInfrastructure = "Comuki.Modules.Repositories.Infrastructure";

    [Fact]
    public void ProceduresDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Procedures.Domain.Catalog.NodeKindCatalog).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ProceduresApplication,
                ProceduresInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                "Comuki.Shared.Contracts",
                IdentityDomain,
                IdentityApplication,
                IdentityInfrastructure,
                ProjectsDomain,
                ProjectsApplication,
                ProjectsInfrastructure,
                RepositoriesDomain,
                RepositoriesApplication,
                RepositoriesInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProceduresApplicationMustNotDependOnOuterLayers()
    {
        // Application sits on Domain through ports only — never reaches
        // EF, the engine or a host. Shared.Contracts is the sanctioned
        // cross-module seam (same allowance as Chat/Artifacts modules);
        // the host composition root is the only place that may reference
        // both Application and Infrastructure.
        var result = Types
            .InAssembly(typeof(Modules.Procedures.Application.Compiler.IProcedureCompiler).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ProceduresInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                IdentityDomain,
                IdentityApplication,
                IdentityInfrastructure,
                ProjectsDomain,
                ProjectsApplication,
                ProjectsInfrastructure,
                RepositoriesDomain,
                RepositoriesApplication,
                RepositoriesInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProceduresInfrastructureMustNotDependOnEngineOrHosts()
    {
        // The Migrator is a composition host and may reference the module;
        // the module must never reference back.
        var result = Types
            .InAssembly(typeof(Modules.Procedures.Infrastructure.Persistence.ProceduresDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                IdentityDomain,
                IdentityApplication,
                IdentityInfrastructure,
                ProjectsDomain,
                ProjectsApplication,
                ProjectsInfrastructure,
                RepositoriesDomain,
                RepositoriesApplication,
                RepositoriesInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
