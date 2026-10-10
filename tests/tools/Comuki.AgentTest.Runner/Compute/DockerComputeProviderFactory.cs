using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers;
using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Comuki.AgentTest.Runner.Compute;

/// <summary>
/// Builds a real <see cref="DockerComputeProvider"/> pointed at Podman —
/// the production seam this works around, not a reimplementation of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Missing production seam.</b>
/// <see cref="Engine.Compute.Installers.ComputeInstaller.AddComukiCompute"/>
/// constructs its <see cref="DockerClient"/> with
/// <c>new DockerClientBuilder().Build()</c> — no <c>.WithEndpoint(...)</c>
/// call, so it resolves the OS-default named pipe (verified by disassembling
/// the pinned <c>Docker.DotNet.Enhanced 4.3.3</c> package: the assembly
/// contains no <c>DOCKER_HOST</c> string literal at all — it does not read
/// that environment variable, unlike Testcontainers' own client). On this
/// repo's Podman-only local runtime that resolves to the wrong named pipe
/// (<c>npipe://./pipe/docker_engine</c> instead of
/// <c>npipe://./pipe/podman-machine-default</c>) and <see cref="DockerComputeOptions"/>
/// has no <c>Endpoint</c>/<c>DockerHost</c> field to override it with. This
/// is a real gap for local Windows/Podman dev of the actual host process,
/// not just this test — flagged in the WS6 report, not fixed here (Compute
/// is explicitly another team's active surface per the WS6 brief).
/// </para>
/// <para>
/// This factory works around it the same way any external consumer of
/// <see cref="DockerComputeProvider"/> would: construct the four
/// constructor dependencies directly with a <see cref="DockerClient"/> built
/// from <c>DOCKER_HOST</c> (default <c>npipe://./pipe/podman-machine-default</c>,
/// this repo's documented Podman endpoint) instead of going through
/// <c>AddComukiCompute</c>'s DI registration. It still exercises the real,
/// unmodified <see cref="DockerComputeProvider"/> class — the "Compute
/// Docker provider provisions a REAL container" proof the WS6 brief asks
/// for — with test-owned wiring instead of a magic DI container.
/// </para>
/// </remarks>
public static class DockerComputeProviderFactory
{
    /// <summary>This repo's documented local Podman endpoint (process-flow.md / WS6 brief) — used when <c>DOCKER_HOST</c> is unset.</summary>
    public const string DefaultPodmanEndpoint = "npipe://./pipe/podman-machine-default";

    /// <summary>
    /// Resolves the Docker/Podman endpoint from <c>DOCKER_HOST</c>, falling
    /// back to <see cref="DefaultPodmanEndpoint"/>.
    /// </summary>
    public static Uri ResolveEndpoint()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        return new Uri(string.IsNullOrWhiteSpace(dockerHost) ? DefaultPodmanEndpoint : dockerHost);
    }

    /// <summary>
    /// The network every worker (and probe) container in this suite joins.
    /// <see cref="DockerComputeOptions.NetworkMode"/> defaults to
    /// <c>bridge</c> — correct for real Docker, but Podman has no network
    /// literally named <c>bridge</c> (verified: <c>podman network ls</c> on
    /// this repo's documented local runtime shows exactly one network,
    /// named <c>podman</c>, driver <c>bridge</c>, subnet
    /// <c>10.88.0.0/16</c>). Passing the Docker default under Podman does
    /// not error, but the container ends up on a network whose gateway
    /// this host is not reachable through — found by exhausting every
    /// <see cref="ContainerHostAddressResolver"/> candidate in this
    /// workstream. Resolved from <see cref="ResolveEndpoint"/> the same
    /// way the client endpoint is: <c>npipe:</c>/<c>unix:</c> paths naming
    /// a Podman machine/socket use <c>podman</c>, anything else keeps
    /// Docker's <c>bridge</c> default.
    /// </summary>
    public static string ResolveNetworkMode()
    {
        var endpoint = ResolveEndpoint().ToString();
        return endpoint.Contains("podman", StringComparison.OrdinalIgnoreCase) ? "podman" : "bridge";
    }

    /// <summary>Builds a <see cref="DockerClient"/> pointed at the resolved endpoint.</summary>
    public static DockerClient CreateClient()
    {
        return new DockerClientBuilder().WithEndpoint(ResolveEndpoint()).Build();
    }

    /// <summary>
    /// Builds a real <see cref="DockerComputeProvider"/> against
    /// <paramref name="client"/> — unfenced (<see cref="ComputeOptions.AllowUnfencedEgress"/>
    /// true) since this suite never asserts the egress fence, and
    /// <paramref name="runAsUser"/> must match the test worker image's
    /// <c>USER</c> directive.
    /// </summary>
    /// <param name="client">A Docker/Podman client — see <see cref="CreateClient"/>.</param>
    /// <param name="runAsUser">Uid (or uid:gid) the container runs as — must match the test worker image.</param>
    /// <param name="maxWorkers">Upper bound of concurrent worker containers this provider will start.</param>
    public static DockerComputeProvider Create(DockerClient client, string runAsUser, int maxWorkers = 4)
    {
        var providerOptions = Options.Create(new ComputeOptions
        {
            Provider = ComputeOptions.DockerProvider,
            AllowUnfencedEgress = true,
        });
        var dockerOptions = Options.Create(new DockerComputeOptions
        {
            NetworkMode = ResolveNetworkMode(),
            FencedNetwork = null,
            MaxWorkers = maxWorkers,
            WaitBeforeKillSeconds = 5,
            RunAsUser = runAsUser,
        });
        var fence = new DockerEgressFence(client.Networks, NullLogger<DockerEgressFence>.Instance);

        return new DockerComputeProvider(fence, client.Containers, providerOptions, dockerOptions);
    }
}
