using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Comuki.Platform.Architecture.Tests;

public sealed class LayerDependencyTests
{
    /// <summary>
    /// Api layer must not reach into Database directly.
    /// All persistence goes through feature/Orchestration or models/Contracts.
    /// </summary>
    [Fact]
    public void ApiPublic_must_not_reference_Database()
    {
        // Load by assembly name to avoid the dotted-namespace resolution issue
        // (Comuki.Platform.Api.Public is a sibling of Comuki.Platform.Api.Contracts,
        //  not a sub-namespace, so typeof(X.Program) fails).
        var apiPublicAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Comuki.Platform.Api.Public", StringComparison.Ordinal))
            ?? Assembly.Load("Comuki.Platform.Api.Public");

        var result = Types
            .InAssembly(apiPublicAssembly)
            .That()
            .ResideInNamespace("Comuki.Platform.Api.Public")
            .ShouldNot()
            .HaveDependencyOn("Comuki.Platform.Database")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Api.Public types depend on Database: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// Models layer must not reference Database (models don't know about EF).
    /// </summary>
    [Fact]
    public void Models_must_not_reference_Database()
    {
        var result = Types
            .InAssembly(typeof(Comuki.Platform.Entity.Core.Run).Assembly)
            .That()
            .ResideInNamespace("Comuki.Platform")
            .ShouldNot()
            .HaveDependencyOn("Comuki.Platform.Database")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Model types depend on Database: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// Feature layer must not reference application layer (no upward deps).
    /// </summary>
    [Fact]
    public void Feature_must_not_reference_Api()
    {
        var result = Types
            .InAssembly(typeof(Comuki.Platform.Orchestration.NoOpOrchestrationService).Assembly)
            .That()
            .ResideInNamespace("Comuki.Platform.Orchestration")
            .ShouldNot()
            .HaveDependencyOn("Comuki.Platform.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Orchestration types depend on Api.Public: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
