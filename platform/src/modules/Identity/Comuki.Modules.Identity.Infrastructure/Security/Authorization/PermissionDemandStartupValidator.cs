using System.Reflection;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Microsoft.Extensions.Hosting;

namespace Comuki.Modules.Identity.Infrastructure.Security.Authorization;

/// <summary>
/// Startup half of <see cref="RequiresPermissionAttribute"/>: every key
/// demanded in the given assemblies must be declared by the
/// <see cref="IPermissionCatalog"/>, or the host refuses to start. A
/// typo'd or undeclared key is a failed boot on whoever shipped it — not
/// a silent allow/deny discovered by a caller.
/// </summary>
/// <param name="permissions"></param>
/// <param name="assemblies">The assemblies whose controllers/endpoints are scanned for demands.</param>
public sealed class PermissionDemandStartupValidator(
    IPermissionCatalog permissions,
    IReadOnlyList<Assembly> assemblies) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var unknown = DemandsOfScannedAssemblies()
            .Where(demand => !IsDeclared(demand.Key))
            .ToList();

        if (unknown.Count != 0)
        {
            var failures = string.Join("; ", unknown.Select(static demand => $"'{demand.Key}' demanded by {demand.Source}"));

            throw new InvalidOperationException(
                $"undeclared permission key(s): {failures}. Either the key is misspelled or the RoleMatrix never "
                + $"declares it. Known keys: {string.Join(", ", permissions.AllKeys.Select(static key => key.Value))}.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private IEnumerable<(string Key, string Source)> DemandsOfScannedAssemblies()
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var attribute in type.GetCustomAttributes<RequiresPermissionAttribute>(inherit: true))
                {
                    yield return (attribute.PermissionKey, type.Name);
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (var attribute in method.GetCustomAttributes<RequiresPermissionAttribute>(inherit: true))
                    {
                        yield return (attribute.PermissionKey, $"{type.Name}.{method.Name}");
                    }
                }
            }
        }
    }

    private bool IsDeclared(string key)
    {
        return PermissionKey.IsWellFormed(key) && permissions.Contains(new PermissionKey(key));
    }
}
