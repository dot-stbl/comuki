using Comuki.Engine.Compute.Environments.Pinning;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Catalog-side Production gate (add-worker-environments task 2.1;
/// worker-environments spec §"Production refuses a tag-only bundle").
/// Mirrors <see cref="WorkerImagePinningShould"/>: tag-only references
/// are the dev / stage path; <c>Production</c> requires a digest. The
/// gate lives where the worker is actually started; this helper is the
/// single source of truth the gate calls.
/// </summary>
public sealed class EnvironmentBundlePinningShould
{
    [Theory(DisplayName = "Given a digest-pinned image, when IsStartable, then always allowed (dev and Production)")]
    [InlineData("custom/worker@sha256:beef")]
    [InlineData("ghcr.io/comuki/env/net10-sdk-bun@sha256:0123456789abcdef")]
    [InlineData("registry.hybrid.ai:5000/env/cpp@sha256:feedface")]
    public void DigestPinnedImageIsAlwaysStartable(string image)
    {
        EnvironmentBundlePinning.IsStartable(image, isProduction: true).ShouldBeTrue();
        EnvironmentBundlePinning.IsStartable(image, isProduction: false).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a tag-only image, when IsStartable in Production, then refused")]
    [InlineData("ghcr.io/comuki/env/net10-sdk-bun:latest")]
    [InlineData("ghcr.io/comuki/env/net10-sdk:v0.1.0")]
    [InlineData("registry:5000/img:v2")]
    public void TagOnlyImageIsRefusedInProduction(string image)
    {
        EnvironmentBundlePinning.IsStartable(image, isProduction: true).ShouldBeFalse();
    }

    [Theory(DisplayName = "Given a tag-only image, when IsStartable in dev/stage, then allowed")]
    [InlineData("ghcr.io/comuki/env/net10-sdk-bun:latest")]
    [InlineData("ghcr.io/comuki/env/net10-sdk:v0.1.0")]
    [InlineData("registry:5000/img:v2")]
    public void TagOnlyImageIsAllowedInDevStage(string image)
    {
        EnvironmentBundlePinning.IsStartable(image, isProduction: false).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an untagged image, when IsStartable in Production, then refused")]
    public void UntaggedImageIsRefusedInProduction()
    {
        EnvironmentBundlePinning.IsStartable("ghcr.io/comuki/env/net10-sdk-bun", isProduction: true).ShouldBeFalse();
    }
}
