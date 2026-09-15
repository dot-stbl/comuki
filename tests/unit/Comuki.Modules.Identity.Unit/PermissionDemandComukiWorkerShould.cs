using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Infrastructure.Security.Authorization;
using Comuki.Shared.Bootstrap.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Startup validation (T4.4): demands declared by the catalog pass; an
/// undeclared key fails the worker cycle with the source named (the
/// registry surfaces it as an unhealthy permission-validate row instead
/// of a dead host).
/// </summary>
public sealed class PermissionDemandComukiWorkerShould
{
    private readonly IPermissionCatalog catalog = new RoleMatrixPermissionCatalog();

    [Fact(DisplayName = "Given a demand no role declares, when the worker runs once, then the cycle fails with the source named")]
    public async Task FailUndeclaredDemandAsync()
    {
        var worker = new PermissionDemandComukiWorker(catalog, [typeof(BogusMarkedController).Assembly]);

        var result = await worker.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
        result.Detail.ShouldContain("typo:fly");
        result.Detail.ShouldContain("BogusMarkedController");
    }

    [Fact(DisplayName = "Given an assembly with no demands, when the worker runs once, then the cycle succeeds")]
    public async Task PassAssemblyWithoutDemandsAsync()
    {
        var worker = new PermissionDemandComukiWorker(catalog, [typeof(PermissionKey).Assembly]);

        var result = await worker.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
    }

    private static WorkerContext NewContext()
    {
        return new WorkerContext(
            new ServiceCollection().BuildServiceProvider(),
            TimeProvider.System,
            NullLogger.Instance);
    }

    [RequiresPermission("typo:fly")]
    private sealed class BogusMarkedController;
}
