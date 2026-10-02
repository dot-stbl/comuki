using Comuki.Engine.Compute.Environments.Pinning;
using Comuki.Engine.Compute.Environments.Shape;

namespace Comuki.Engine.Compute.Environments.Catalog;

/// <summary>
/// Default in-process catalog seeded with the two Comuki golden classes
/// (worker-environments spec §"Golden class net10-sdk-bun"): Linux,
/// .NET 10 SDK, bun &gt;= 1.4 for <c>net10-sdk-bun</c>; Linux, .NET 10
/// SDK only for <c>net10-sdk</c>. Both entries ship tag-only (the local
/// dev path); the gate at <see cref="EnvironmentBundlePinning.IsStartable"/>
/// refuses them in <c>Production</c> until the operator pins a digest
/// (task 1.1 — the golden bundle Dockerfile is the digest source).
///
/// <see cref="TryGet"/> / <see cref="List"/> are read-only — write of
/// catalog entries is an operator/fleet concern (worker-environments spec
/// §"Bundle catalog is readable"), not a Brain tool. The
/// <see cref="IsAllowed"/> gate is the fleet allowlist: a community
/// publisher whose name is not on the allowlist cannot start a worker
/// (task 6.2; spec §"Unallowlisted community bundle is not started").
/// </summary>
/// <param name="seed">Bundles the catalog exposes — order does not matter; the catalog sorts by id on read.</param>
/// <param name="allowedPublishers">Publisher names the fleet admits. Defaults to the Comuki shelf only (community / org are refused until the operator adds them). Comparison is case-insensitive.</param>
public sealed class DefaultEnvironmentCatalog(
    IEnumerable<EnvironmentBundle> seed,
    IReadOnlySet<string>? allowedPublishers = null) : IEnvironmentCatalog
{
    /// <summary>Catalog id of the SDK-only Linux class.</summary>
    public const string Net10SdkId = "net10-sdk";

    /// <summary>Catalog id of the SDK + bun Linux class (the Comuki golden class).</summary>
    public const string Net10SdkBunId = "net10-sdk-bun";

    /// <summary>Fleet default allowlist: only the Comuki shelf pulls (community / org are refused until the operator opts in).</summary>
    public static readonly IReadOnlySet<string> DefaultAllowedPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "comuki",
    };

    private readonly Dictionary<string, EnvironmentBundle> bundles =
        seed.ToDictionary(static bundle => bundle.Id, StringComparer.Ordinal);

    private readonly IReadOnlySet<string> allowedPublishers = allowedPublishers ?? DefaultAllowedPublishers;

    /// <summary>
    /// Builds the catalog from the seeded golden classes. Both entries
    /// are Linux / <see cref="EnvironmentPublisher.Comuki"/>; the resource
    /// shape is left to the host (no <c>Cpus</c> / <c>Memory</c> bound,
    /// no <c>Gpu</c>). The default fleet allowlist admits the Comuki
    /// shelf only.
    /// </summary>
    public DefaultEnvironmentCatalog()
        : this(EnvironmentCatalogSeed.Defaults)
    {
    }

    /// <inheritdoc />
    public bool TryGet(string id, out EnvironmentBundle? bundle)
    {
        if (id is null)
        {
            bundle = null;
            return false;
        }

        return bundles.TryGetValue(id, out bundle);
    }

    /// <inheritdoc />
    public IReadOnlyList<EnvironmentBundle> List()
    {
        return [.. bundles.Values.OrderBy(static bundle => bundle.Id, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public bool IsAllowed(string publisher)
    {
        return !string.IsNullOrEmpty(publisher) && allowedPublishers.Contains(publisher);
    }
}

/// <summary>The two Comuki golden classes this catalog ships by default.</summary>
file static class EnvironmentCatalogSeed
{
    public static readonly IReadOnlyList<EnvironmentBundle> Defaults =
    [
        new EnvironmentBundle(
            Id: DefaultEnvironmentCatalog.Net10SdkId,
            Image: "ghcr.io/comuki/env/net10-sdk:latest",
            Runtime: EnvironmentRuntime.Linux,
            Publisher: EnvironmentPublisher.Comuki,
            RestoreOpcodes: ["dotnet"],
            ResourceShape: new EnvironmentResourceShape(Cpus: null, Memory: null, Gpu: false)),
        new EnvironmentBundle(
            Id: DefaultEnvironmentCatalog.Net10SdkBunId,
            Image: "ghcr.io/comuki/env/net10-sdk-bun:latest",
            Runtime: EnvironmentRuntime.Linux,
            Publisher: EnvironmentPublisher.Comuki,
            RestoreOpcodes: ["dotnet", "bun"],
            ResourceShape: new EnvironmentResourceShape(Cpus: null, Memory: null, Gpu: false)),
    ];
}
