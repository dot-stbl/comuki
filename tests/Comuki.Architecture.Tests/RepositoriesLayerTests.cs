using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Repositories module (comuki-project-structure.md §2):
/// Domain is the innermost layer, Application sits on it through ports,
/// Infrastructure wires EF, and no module reaches into the engine or the
/// hosts. Sibling-module isolation matches the spec — no reference to
/// Compute, Projects, or Orchestration implementations from anywhere in
/// the Repositories module. The Application-layer test lands with
/// workstream 2 once that assembly gains types.
/// </summary>
public sealed class RepositoriesLayerTests
{
    private const string RepositoriesApplication = "Comuki.Modules.Repositories.Application";
    private const string RepositoriesInfrastructure = "Comuki.Modules.Repositories.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";
    private const string ProjectsDomain = "Comuki.Modules.Projects.Domain";
    private const string ProjectsApplication = "Comuki.Modules.Projects.Application";
    private const string ProjectsInfrastructure = "Comuki.Modules.Projects.Infrastructure";

    [Fact]
    public void RepositoriesDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(Modules.Repositories.Domain.Ids.RepositoryId).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                RepositoriesApplication,
                RepositoriesInfrastructure,
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                "Comuki.Shared.Contracts",
                ProjectsDomain,
                ProjectsApplication,
                ProjectsInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void RepositoriesInfrastructureMustNotDependOnEngineOrHosts()
    {
        // The Migrator is a composition host and may reference the module;
        // the module must never reference back.
        var result = Types
            .InAssembly(typeof(Modules.Repositories.Infrastructure.Persistence.RepositoriesDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                Engine,
                EngineCompute,
                Host,
                Translator,
                Migrator,
                ProjectsDomain,
                ProjectsApplication,
                ProjectsInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
