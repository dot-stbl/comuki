using System.Reflection;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Shared.Bootstrap.Workers;

namespace Comuki.Modules.Identity.Infrastructure.Security.Authorization;

/// <summary>
/// Startup half of <see cref="RequiresPermissionAttribute"/> behind the
/// comuki worker registry: every key demanded in the given assemblies must
/// be declared by the <see cref="IPermissionCatalog"/>, or the worker
/// reports a failed cycle. A typo'd or undeclared key used to crash the
/// boot; it now surfaces as an error-level registry failure and an
/// unhealthy <c>permission-validate</c> row on
/// <c>/api/v1/workers/background</c> — visible on every deploy instead of
/// a silent allow/deny discovered by a caller.
/// </summary>
/// <param name="permissions">The declared-permission catalog.</param>
/// <param name="assemblies">The assemblies whose controllers/endpoints are scanned for demands.</param>
public sealed class PermissionDemandComukiWorker(
    IPermissionCatalog permissions,
    IReadOnlyList<Assembly> assemblies) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "permission-validate";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Startup();

    /// <inheritdoc />
    public Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        var unknown = PermissionDemandScanner.UndeclaredDemands(assemblies, permissions);
        if (unknown.Count != 0)
        {
            var failures = string.Join("; ", unknown.Select(static demand => $"'{demand.Key}' demanded by {demand.Source}"));

            return Task.FromResult(WorkerResult.Fail(
                $"undeclared permission key(s): {failures}. Either the key is misspelled or the RoleMatrix never "
                + $"declares it. Known keys: {string.Join(", ", permissions.AllKeys.Select(static key => key.Value))}."));
        }

        return Task.FromResult(WorkerResult.Ok("all demands declared"));
    }
}

/// <summary>
/// File-scoped reflection scan behind <see cref="PermissionDemandComukiWorker"/>:
/// every <see cref="RequiresPermissionAttribute"/> on the scanned
/// assemblies' types and methods, filtered down to the keys the catalog
/// does not declare. A single static class keeps the worker free of
/// private methods (rule code-shape §9).
/// </summary>
file static class PermissionDemandScanner
{
    /// <summary>
    /// Lists every demanded-but-undeclared permission key with the type or
    /// member that demanded it.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan.</param>
    /// <param name="permissions">The declared-permission catalog.</param>
    /// <returns>Undeclared demands with their source; empty when everything is declared.</returns>
    public static IReadOnlyList<(string Key, string Source)> UndeclaredDemands(
        IReadOnlyList<Assembly> assemblies,
        IPermissionCatalog permissions)
    {
        return [.. DemandsOfScannedAssemblies(assemblies)
            .Where(demand => !IsDeclared(demand.Key, permissions))];
    }

    /// <summary>Every <see cref="RequiresPermissionAttribute"/> demand on the scanned assemblies' types and methods.</summary>
    /// <param name="assemblies">Assemblies to scan.</param>
    /// <returns>Demand key + the type or member that demanded it.</returns>
    public static IEnumerable<(string Key, string Source)> DemandsOfScannedAssemblies(IReadOnlyList<Assembly> assemblies)
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

    /// <summary>Whether the catalog declares the demanded key.</summary>
    /// <param name="key">The demanded permission key.</param>
    /// <param name="permissions">The declared-permission catalog.</param>
    /// <returns>True when declared (and well-formed).</returns>
    public static bool IsDeclared(string key, IPermissionCatalog permissions)
    {
        return PermissionKey.IsWellFormed(key) && permissions.Contains(new PermissionKey(key));
    }
}
