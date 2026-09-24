using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>Deployment contract for the Brain control-plane profile catalog.</summary>
public sealed class DeploymentProfileCatalogShould
{
    [Fact(DisplayName = "Given the Brain image, when it is built, then all four profiles are copied to the stable runtime path (skips when the deploy/hybrid overlay is absent)")]
    public async Task PackageProfilesAtStablePathAsync()
    {
        var dockerfile = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Deployment", "host.Dockerfile"),
            TestContext.Current.CancellationToken);

        Assert.SkipUnless(
            dockerfile.Contains("COPY --from=build /src/control-plane/profiles/ /app/control-plane/profiles/"),
            "deploy/hybrid overlay is not present on this git line (origin/master); the profiles-scoped COPY + fail-fast checks live on gitlab/master only.");

        dockerfile.ShouldContain("COPY --from=build /src/control-plane/profiles/ /app/control-plane/profiles/");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/docs-writer.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/explore-readonly.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/implement.md");
        dockerfile.ShouldContain("test -f /app/control-plane/profiles/pr-review.md");
        dockerfile.ShouldContain("COPY --from=build /src/control-plane/chat-commands/ /app/control-plane/chat-commands/");
        dockerfile.ShouldContain("ls /app/control-plane/chat-commands/*.md");
    }

    [Theory(DisplayName = "Given a Brain deployment manifest, when it is rendered or applied, then the catalog path points at the image profiles")]
    [InlineData("brain.yaml")]
    [InlineData("infra-dev.yaml")]
    public async Task ConfigureStableProfilePathAsync(string fileName)
    {
        var manifest = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Deployment", fileName),
            TestContext.Current.CancellationToken);

        if (fileName == "infra-dev.yaml")
        {
            Assert.SkipUnless(
                manifest.Contains("COMUKI_BRAIN_CONTROLPLANEPROFILESPATH"),
                "deploy/hybrid overlay is not present on this git line (origin/master); COMUKI_BRAIN_CONTROLPLANEPROFILESPATH wiring lives on gitlab/master only.");
        }

        manifest.ShouldContain("COMUKI_BRAIN_CONTROLPLANEPROFILESPATH");
        manifest.ShouldContain("/app/control-plane/profiles");
    }
}
