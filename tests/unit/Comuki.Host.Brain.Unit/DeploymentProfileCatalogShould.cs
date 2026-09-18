using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>Deployment contract for the Brain control-plane profile catalog.</summary>
public sealed class DeploymentProfileCatalogShould
{
    [Fact(DisplayName = "Given the Brain image, when it is built, then all four profiles are copied to the stable runtime path")]
    public async Task PackageProfilesAtStablePathAsync()
    {
        var dockerfile = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Deployment", "host.Dockerfile"),
            TestContext.Current.CancellationToken);

        dockerfile.ShouldContain("COPY --from=build /src/control-plane/profiles/ /app/control-plane/profiles/");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/docs-writer.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/explore-readonly.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/implement.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/pr-review.md");
    }

    [Theory(DisplayName = "Given a Brain deployment manifest, when it is rendered or applied, then the catalog path points at the image profiles")]
    [InlineData("brain.yaml")]
    [InlineData("infra-dev.yaml")]
    public async Task ConfigureStableProfilePathAsync(string fileName)
    {
        var manifest = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Deployment", fileName),
            TestContext.Current.CancellationToken);

        manifest.ShouldContain("COMUKI_BRAIN_CONTROLPLANEPROFILESPATH");
        manifest.ShouldContain("/app/control-plane/profiles");
    }
}
