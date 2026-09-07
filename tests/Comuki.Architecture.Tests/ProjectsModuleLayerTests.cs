using Comuki.Modules.Projects.Application.Admission;
using Comuki.Modules.Projects.Domain.DomainTypes;
using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Layer rules for the Projects module (comuki-project-structure.md §2 and
/// <c>architecture.md</c> laws 1–3): Domain is innermost and framework-free,
/// Application sits on it through ports, and neither layer reaches into a
/// sibling module, the engine or a host. The domain-user intake work added
/// a second aggregate to the module, so the boundary is asserted, not
/// assumed.
/// </summary>
public sealed class ProjectsModuleLayerTests
{
    private const string ProjectsApplication = "Comuki.Modules.Projects.Application";
    private const string ProjectsInfrastructure = "Comuki.Modules.Projects.Infrastructure";
    private const string Engine = "Comuki.Engine.Orchestration";
    private const string EngineCompute = "Comuki.Engine.Compute";
    private const string Host = "Comuki.Host";
    private const string Translator = "Comuki.Host.Translator";
    private const string Migrator = "Comuki.Migrator";
    private const string SiblingModules = "Comuki.Modules.Intake";

    [Fact]
    public void ProjectsDomainMustNotDependOnOuterLayers()
    {
        var result = Types
            .InAssembly(typeof(DomainTypeAdmission).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ProjectsApplication,
                ProjectsInfrastructure,
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
    public void ProjectsDomainMustNotDependOnEntityFramework()
    {
        // Law 2: the innermost layer carries no persistence framework — the
        // EF model lives in Infrastructure configurations.
        var result = Types
            .InAssembly(typeof(DomainTypeAdmission).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProjectsApplicationMustNotDependOnInfrastructureOrEngineOrHosts()
    {
        var result = Types
            .InAssembly(typeof(DomainTypeAdmissionService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ProjectsInfrastructure, Engine, EngineCompute, Host, Translator, Migrator)
            .GetResult();

        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Fact]
    public void ProjectsModuleMustNotDependOnSiblingModules()
    {
        // Law 3: the admission policy stores intake source keys as plain
        // strings precisely so the module never references Intake.
        var domain = Types
            .InAssembly(typeof(DomainTypeAdmission).Assembly)
            .ShouldNot()
            .HaveDependencyOn(SiblingModules)
            .GetResult();
        var application = Types
            .InAssembly(typeof(DomainTypeAdmissionService).Assembly)
            .ShouldNot()
            .HaveDependencyOn(SiblingModules)
            .GetResult();

        Assert.True(domain.IsSuccessful, Failing(domain));
        Assert.True(application.IsSuccessful, Failing(application));
    }

    private static string Failing(NetArchTest.Rules.TestResult result)
    {
        return $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
    }
}
