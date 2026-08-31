using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Modules.Identity.Infrastructure.Security.Authorization;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Startup validation (T4.4): demands declared by the catalog pass;
/// an undeclared key fails the boot with the source named.
/// </summary>
public sealed class PermissionDemandStartupValidatorShould
{
    private readonly IPermissionCatalog catalog = new RoleMatrixPermissionCatalog();

    [Fact(DisplayName = "Given a demand no role declares, when the validator starts, then it throws with the source")]
    public async Task FailUndeclaredDemandAsync()
    {
        var validator = new PermissionDemandStartupValidator(catalog, [typeof(BogusMarkedController).Assembly]);

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => validator.StartAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("typo:fly");
        failure.Message.ShouldContain("BogusMarkedController");
    }

    [Fact(DisplayName = "Given an assembly with no demands, when the validator starts, then it passes")]
    public async Task PassAssemblyWithoutDemandsAsync()
    {
        var validator = new PermissionDemandStartupValidator(catalog, [typeof(PermissionKey).Assembly]);

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [RequiresPermission("typo:fly")]
    private sealed class BogusMarkedController;
}
