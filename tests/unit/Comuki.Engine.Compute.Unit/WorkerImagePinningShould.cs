using Comuki.Engine.Compute.Options;
using Comuki.Shared.Bootstrap.Versioning;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="WorkerImagePinning"/> — the release contract
/// (RELEASE.md): untagged worker image → pinned to the host build version;
/// explicit tag / digest untouched; unstamped build → <c>latest</c>.
/// </summary>
public sealed class WorkerImagePinningShould
{
    private static ComukiBuildInformation Build(string version)
    {
        return new ComukiBuildInformation(version, "abc1234", "2026-09-14", "release");
    }

    [Fact(DisplayName = "Given untagged image, when Resolve, then appends the host build version as the tag")]
    public void PinUntaggedImageToBuildVersion()
    {
        var resolved = WorkerImagePinning.Resolve("ghcr.io/dot-stbl/comuki-worker", Build("1.2.3"));

        resolved.ShouldBe("ghcr.io/dot-stbl/comuki-worker:1.2.3");
    }

    [Fact(DisplayName = "Given version with a leading v, when Resolve, then strips the v from the tag")]
    public void StripLeadingVFromVersion()
    {
        var resolved = WorkerImagePinning.Resolve("ghcr.io/dot-stbl/comuki-worker", Build("v0.4.1"));

        resolved.ShouldBe("ghcr.io/dot-stbl/comuki-worker:0.4.1");
    }

    [Fact(DisplayName = "Given image with an explicit tag, when Resolve, then the image is untouched")]
    public void LeaveExplicitTagAlone()
    {
        var resolved = WorkerImagePinning.Resolve("comuki-worker:local", Build("1.2.3"));

        resolved.ShouldBe("comuki-worker:local");
    }

    [Fact(DisplayName = "Given image pinned by digest, when Resolve, then the image is untouched")]
    public void LeaveDigestPinnedImageAlone()
    {
        var resolved = WorkerImagePinning.Resolve("custom/worker@sha256:beef", Build("1.2.3"));

        resolved.ShouldBe("custom/worker@sha256:beef");
    }

    [Fact(DisplayName = "Given registry host with a port and no tag, when Resolve, then the port stays a port and the version becomes the tag")]
    public void PinRegistryWithPortHost()
    {
        var resolved = WorkerImagePinning.Resolve("registry.hybrid.ai:5000/comuki/worker", Build("1.2.3"));

        resolved.ShouldBe("registry.hybrid.ai:5000/comuki/worker:1.2.3");
    }

    [Fact(DisplayName = "Given registry host with a port and a tag, when Resolve, then the image is untouched")]
    public void LeaveTaggedRegistryWithPortHostAlone()
    {
        var resolved = WorkerImagePinning.Resolve("registry:5000/img:v2", Build("1.2.3"));

        resolved.ShouldBe("registry:5000/img:v2");
    }

    [Fact(DisplayName = "Given unstamped build (0.0.0), when Resolve, then falls back to latest")]
    public void FallBackToLatestForUnstampedBuild()
    {
        var resolved = WorkerImagePinning.Resolve("ghcr.io/dot-stbl/comuki-worker", Build("0.0.0"));

        resolved.ShouldBe("ghcr.io/dot-stbl/comuki-worker:latest");
    }

    [Fact(DisplayName = "Given empty build version, when Resolve, then falls back to latest")]
    public void FallBackToLatestForEmptyVersion()
    {
        var resolved = WorkerImagePinning.Resolve("ghcr.io/dot-stbl/comuki-worker", Build(""));

        resolved.ShouldBe("ghcr.io/dot-stbl/comuki-worker:latest");
    }
}
