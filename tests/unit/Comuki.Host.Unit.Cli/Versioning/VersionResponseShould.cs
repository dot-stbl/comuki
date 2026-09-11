using Comuki.Host.Versioning;
using Comuki.Shared.Bootstrap.Versioning;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Versioning;

/// <summary>
/// The build-info endpoint payload (issue #56 §6): VersionResponse maps
/// the bootstrap build information onto the wire shape, and the route
/// constant pins the anonymous /api/v1/version surface.
/// </summary>
public sealed class VersionResponseShould
{
    [Fact(DisplayName = "Given full build information, when mapped, then the payload carries version, sha, build date and lowercased mode")]
    public void MapFullBuildInformation()
    {
        var response = VersionResponse.From(new ComukiBuildInformation(
            "1.0.0",
            "41d265deae51dbd82291ef85a376a4b123d0521c",
            "2026-09-11",
            "Debug"));

        response.ShouldBe(new VersionResponse(
            "1.0.0",
            "41d265deae51dbd82291ef85a376a4b123d0521c",
            "2026-09-11",
            "debug"));
    }

    [Fact(DisplayName = "Given Unknown build information, when mapped, then absent segments stay null and the mode reads unknown")]
    public void MapUnknownBuildInformation()
    {
        var response = VersionResponse.From(ComukiBuildInformation.Unknown);

        response.Version.ShouldBe("0.0.0");
        response.Sha.ShouldBeNull();
        response.BuildDate.ShouldBeNull();
        response.Mode.ShouldBe("unknown");
    }

    [Fact(DisplayName = "Given the route table, when the version endpoint is mapped, then the route is pinned as a constant")]
    public void RouteConstantIsPinned()
    {
        ApiRoutes.Version.ShouldBe("/api/v1/version");
    }
}
