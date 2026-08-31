using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;

namespace Comuki.Host.Auth;

/// <summary>
/// Creates the bootstrap admin when the environment asks for one:
/// account absent → create + grant platform-admin; account present →
/// nothing. Idempotent by construction — safe to run on every boot.
/// Scoped: it rides the scoped handlers and stores.
/// </summary>
/// <param name="options"></param>
/// <param name="createUser"></param>
/// <param name="grantRole"></param>
/// <param name="users"></param>
/// <param name="logger"></param>
public sealed class BootstrapAdminSeeder(
    BootstrapAdminOptions options,
    CreateUserHandler createUser,
    GrantRoleHandler grantRole,
    IUserAccountStore users,
    ILogger<BootstrapAdminSeeder> logger)
{
    /// <summary>Runs one idempotent bootstrap pass.</summary>
    /// <param name="cancellationToken"></param>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (options.AdminEmail is not { } email || options.AdminPassword is not { } password)
        {
            logger.LogDebug("Bootstrap admin not configured; skipping");
            return;
        }

        if (await users.FindByEmailAsync(email, cancellationToken) is { } existing)
        {
            logger.LogInformation(
                "Bootstrap admin {Email} already exists as user {UserId}; nothing to do",
                email,
                existing.Id.Value);
            return;
        }

        var created = await createUser.HandleAsync(
            new CreateUserCommand(email, email, password),
            cancellationToken);
        _ = await grantRole.HandleAsync(
            new GrantRoleCommand(
                RoleSubject.ForUser(created.Id),
                Role.PlatformAdmin,
                AssignmentScope.Platform(),
                ActingAs: null),
            cancellationToken);

        logger.LogInformation(
            "Bootstrap admin {Email} created as user {UserId} and granted platform-admin",
            email,
            created.Id.Value);
    }
}
