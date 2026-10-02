using Comuki.Engine.Compute.Environments;
using Comuki.Engine.Compute.Environments.Catalog;
using Comuki.Engine.Compute.Environments.Shape;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Default-catalog seeding and lookup surface (add-worker-environments
/// task 2.1): the two golden classes ship at construction,
/// <c>TryGet</c> is hit-or-miss, <c>List</c> returns sorted-by-id. The
/// Production-refusal gate is tested in
/// <see cref="EnvironmentBundlePinningShould"/>.
/// </summary>
public sealed class DefaultEnvironmentCatalogShould
{
    [Fact(DisplayName = "When list the catalog, then the two golden Comuki classes appear with Linux runtime, restore opcodes, and Comuki publisher")]
    public void SeedGoldenClassesOnConstruction()
    {
        var catalog = new DefaultEnvironmentCatalog();

        var list = catalog.List();

        list.Count.ShouldBe(2);

        var sdk = list.Single(static bundle => bundle.Id == DefaultEnvironmentCatalog.Net10SdkId);
        sdk.Runtime.ShouldBe(EnvironmentRuntime.Linux);
        sdk.Publisher.ShouldBe(EnvironmentPublisher.Comuki);
        sdk.RestoreOpcodes.ShouldBe(["dotnet"]);
        sdk.HasDigest.ShouldBeFalse();

        var sdkBun = list.Single(static bundle => bundle.Id == DefaultEnvironmentCatalog.Net10SdkBunId);
        sdkBun.Runtime.ShouldBe(EnvironmentRuntime.Linux);
        sdkBun.Publisher.ShouldBe(EnvironmentPublisher.Comuki);
        sdkBun.RestoreOpcodes.ShouldBe(["dotnet", "bun"]);
        sdkBun.HasDigest.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a known id, when TryGet, then returns the seeded bundle")]
    public void TryGetHitsForSeededClass()
    {
        var catalog = new DefaultEnvironmentCatalog();

        var found = catalog.TryGet(DefaultEnvironmentCatalog.Net10SdkBunId, out var bundle);

        found.ShouldBeTrue();
        bundle.ShouldNotBeNull();
        bundle!.Id.ShouldBe(DefaultEnvironmentCatalog.Net10SdkBunId);
        bundle.RestoreOpcodes.ShouldBe(["dotnet", "bun"]);
    }

    [Fact(DisplayName = "Given an unknown id, when TryGet, then misses and returns a null bundle")]
    public void TryGetMissesForUnknownClass()
    {
        var catalog = new DefaultEnvironmentCatalog();

        var found = catalog.TryGet("cpp-clang-99-ultralinux", out var bundle);

        found.ShouldBeFalse();
        bundle.ShouldBeNull();
    }

    [Fact(DisplayName = "Given multiple bundles with digest-pinned images, when listed, then HasDigest reports true")]
    public void DigestPinnedBundlesReportHasDigest()
    {
        var catalog = new DefaultEnvironmentCatalog(
        [
            new EnvironmentBundle(
                Id: "cpp-clang-18-linux",
                Image: "ghcr.io/comuki/env/cpp-clang-18-linux@sha256:beef",
                Runtime: EnvironmentRuntime.Linux,
                Publisher: EnvironmentPublisher.Comuki,
                RestoreOpcodes: ["cmake"],
                ResourceShape: new EnvironmentResourceShape(Cpus: 4, Memory: "8Gi", Gpu: false)),
        ]);

        var found = catalog.TryGet("cpp-clang-18-linux", out var bundle);

        found.ShouldBeTrue();
        bundle!.HasDigest.ShouldBeTrue();
    }

    [Fact(DisplayName = "When list, then bundles are sorted by id for stable operator reads")]
    public void ListSortsById()
    {
        var catalog = new DefaultEnvironmentCatalog();

        var list = catalog.List();

        list[0].Id.ShouldBe(DefaultEnvironmentCatalog.Net10SdkId);
        list[1].Id.ShouldBe(DefaultEnvironmentCatalog.Net10SdkBunId);
    }

    [Fact(DisplayName = "Given the default fleet, then IsAllowed('comuki') is true (Comuki shelf is on by default)")]
    public void ComukiPublisherIsAlwaysAllowedOnDefaultFleet()
    {
        var catalog = new DefaultEnvironmentCatalog();

        catalog.IsAllowed("comuki").ShouldBeTrue();
    }

    [Fact(DisplayName = "Given the default fleet (Comuki only), when IsAllowed('community'), then refused")]
    public void CommunityPublisherIsRefusedByDefaultFleet()
    {
        var catalog = new DefaultEnvironmentCatalog();

        catalog.IsAllowed("community").ShouldBeFalse();
        catalog.IsAllowed("org").ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an allowlist that admits it, when IsAllowed('community'), then allowed")]
    public void CommunityPublisherIsAllowedWhenOnTheFleetAllowlist()
    {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "comuki", "community" };
        var catalog = new DefaultEnvironmentCatalog(
        [
            new EnvironmentBundle(
                Id: "cpp-clang-18-linux",
                Image: "ghcr.io/community/cpp-clang-18:latest",
                Runtime: EnvironmentRuntime.Linux,
                Publisher: EnvironmentPublisher.Community,
                RestoreOpcodes: ["cmake"],
                ResourceShape: new EnvironmentResourceShape(Cpus: 4, Memory: "8Gi", Gpu: false)),
        ],
        allow);

        catalog.IsAllowed("comuki").ShouldBeTrue();
        catalog.IsAllowed("community").ShouldBeTrue();
        catalog.IsAllowed("org").ShouldBeFalse();
    }
}
