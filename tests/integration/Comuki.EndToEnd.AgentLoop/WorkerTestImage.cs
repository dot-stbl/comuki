using DotNet.Testcontainers.Builders;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// Builds the WS6 test worker image from
/// <c>tests/tools/Comuki.TestFakePi/worker-test.Dockerfile</c> once per
/// test collection, via Testcontainers' own image builder (the same
/// low-level client Comuki.Host.Testing.PostgresCollectionFixture already
/// proves works against this repo's Podman <c>DOCKER_HOST</c>/
/// <c>TESTCONTAINERS_RYUK_DISABLED</c> setup — no separate build-tooling
/// dependency).
/// </summary>
public static class WorkerTestImage
{
    /// <summary>The tag every T2a scenario's <c>worker.image</c> field expects.</summary>
    public const string Tag = "comuki-agent-test-worker:ws6";

    /// <summary>
    /// Builds (or reuses an already-built) <see cref="Tag"/> from the
    /// repo-root build context. Idempotent to call more than once in a
    /// process — Testcontainers/Podman layer-caches the build.
    /// </summary>
    /// <param name="repositoryRoot">The repo root — the Dockerfile's build context.</param>
    /// <param name="cancellationToken"></param>
    public static async Task BuildAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var image = new ImageFromDockerfileBuilder()
            .WithName(Tag)
            .WithDockerfileDirectory(repositoryRoot)
            // Forward slashes deliberately, not Path.Combine: this path is
            // sent to the (Linux) Podman daemon inside the build tar, not
            // resolved on the local filesystem — Path.Combine on Windows
            // would emit backslashes the remote builder cannot resolve.
            .WithDockerfile("tests/tools/Comuki.TestFakePi/worker-test.Dockerfile")
            .WithCleanUp(false)
            .Build();

        await image.CreateAsync(cancellationToken);
    }
}
