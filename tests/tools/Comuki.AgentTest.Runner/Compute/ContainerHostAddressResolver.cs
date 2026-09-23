using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Comuki.AgentTest.Runner.Compute;

/// <summary>
/// Discovers which host address a worker container can reach the test
/// orchestrator host on — the "container-from-host networking on
/// Podman/WSL" investigation the WS6 brief asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Finding.</b> Podman auto-injects <c>host.containers.internal</c> /
/// <c>host.docker.internal</c> into every container's <c>/etc/hosts</c> (no
/// extra container flags needed — confirmed via <c>podman run --rm alpine:3
/// cat /etc/hosts</c>). On native Linux Podman/Docker this resolves and
/// routes straight back to the host and this class's first candidate wins
/// immediately. <b>On this repo's actual local runtime — Podman machine on
/// Windows/WSL2, NAT networking mode (no <c>networkingMode=mirrored</c> in
/// <c>.wslconfig</c>)</b> — that DNS name resolves (to the gvproxy gateway,
/// typically <c>169.254.1.2</c>) but a TCP connect to a host-bound
/// <c>0.0.0.0</c> listener through it is actively refused; a raw listener
/// confirmed live via loopback and 0.0.0.0 was unreachable through
/// <c>host.containers.internal</c> but WAS reachable directly through the
/// Windows host's own <c>vEthernet (WSL ...)</c> adapter IPv4 address
/// (verified with a real container + a real TCP listener during this
/// workstream). The likely cause is the separate Hyper-V/WSL virtual-switch
/// firewall layer interacting with WSL2's default NAT mode — outside this
/// workstream's file scope to fix (it is host machine configuration, not
/// Comuki code); the documented remediation is enabling WSL2 mirrored
/// networking (<c>networkingMode=mirrored</c> in <c>%UserProfile%\.wslconfig</c>,
/// Windows 11 22H2+), which was not attempted here since it changes shared
/// host state outside a worktree-isolated agent's remit.
/// </para>
/// <para>
/// <b>Resolution strategy.</b> Rather than hard-code either assumption, this
/// resolver builds an ordered candidate list — the two <c>host.*.internal</c>
/// DNS names first (works out of the box on native Linux runners), then
/// every "up", non-loopback, non-link-local IPv4 address of a local NIC
/// (WSL/Hyper-V-named adapters first, since that is what worked here) — and
/// empirically probes each from inside a real container against the
/// orchestrator host's <c>/api/v1/health</c> endpoint, in one throwaway
/// container invocation. The first candidate that answers wins; the winner
/// is what gets stamped into <c>COMUKI_ORCH_HTTP</c>/<c>COMUKI_ORCH_GRPC</c>
/// for the real T2a container. This makes the suite self-adapting across a
/// native Linux CI runner and a Windows/WSL2 Podman dev box without either
/// one needing a special-cased config flag.
/// </para>
/// </remarks>
public static class ContainerHostAddressResolver
{
    /// <summary>
    /// Probes every candidate host address from inside one throwaway
    /// container built from <paramref name="probeImage"/> (must have
    /// <c>curl</c> and <c>sh</c> — the test worker image qualifies) and
    /// returns the first one the container could reach.
    /// </summary>
    /// <param name="containers">Docker container operations (create/start/wait/logs/remove) on the Podman-pointed client.</param>
    /// <param name="probeImage">An image with <c>curl</c> on PATH — the test worker image is reused so no extra image pull is needed.</param>
    /// <param name="networkMode">Network mode the probe container joins — must match the real T2a container's so the probe result is representative.</param>
    /// <param name="hostPort">The orchestrator host's bound port.</param>
    /// <param name="healthPath">A cheap GET path the orchestrator always answers (<c>Comuki.Host</c>'s <c>/api/v1/health</c>).</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException">No candidate address was reachable — message lists every candidate tried.</exception>
    public static async Task<string> ResolveAsync(
        IContainerOperations containers,
        string probeImage,
        string networkMode,
        int hostPort,
        string healthPath,
        CancellationToken cancellationToken = default)
    {
        var candidates = BuildCandidates();

        const string script =
            "for candidate in \"$@\"; do "
                + "if curl -sf --max-time 2 -o /dev/null \"http://$candidate:$PORT$HEALTH_PATH\"; then "
                + "echo \"$candidate\"; exit 0; "
                + "fi; "
                + "done; exit 1";

        var created = await containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = probeImage,
                Entrypoint = ["sh"],
                Cmd = ["-c", script, "sh", .. candidates],
                Env = [$"PORT={hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}", $"HEALTH_PATH={healthPath}"],
                HostConfig = new HostConfig { NetworkMode = networkMode, AutoRemove = false },
            },
            cancellationToken);

        try
        {
            await containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);
            var wait = await containers.WaitContainerAsync(created.ID, cancellationToken);

            var logs = await ReadLogsAsync(containers, created.ID, cancellationToken);
            if (wait.StatusCode == 0)
            {
                var winner = logs.Trim();
                if (!string.IsNullOrEmpty(winner))
                {
                    return winner;
                }
            }

            throw new InvalidOperationException(
                "no container-reachable host address found; tried: " + string.Join(", ", candidates)
                    + ". See Comuki.AgentTest.Runner.Compute.ContainerHostAddressResolver's remarks for the "
                    + "Podman/WSL networking investigation and the mirrored-networking remediation.");
        }
        finally
        {
            await containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
        }
    }

    private static async Task<string> ReadLogsAsync(IContainerOperations containers, string containerId, CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder();
        var progress = new Progress<string>(line => builder.Append(line).Append('\n'));
        await containers.GetContainerLogsAsync(
            containerId,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = false },
            progress,
            cancellationToken);
        return builder.ToString();
    }

    /// <summary>
    /// The two well-known Podman/Docker host DNS names first, then every
    /// up/non-loopback/non-link-local local IPv4 (WSL/Hyper-V-named
    /// adapters ordered first — see the class remarks for why).
    /// </summary>
    private static List<string> BuildCandidates()
    {
        var dnsNames = new List<string> { "host.containers.internal", "host.docker.internal" };
        var wslAdapterAddresses = new List<string>();
        var otherAddresses = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var isWslLike = nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase)
                || nic.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase)
                || nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(address)
                    || IsLinkLocal(address))
                {
                    continue;
                }

                (isWslLike ? wslAdapterAddresses : otherAddresses).Add(address.ToString());
            }
        }

        return [.. dnsNames, .. wslAdapterAddresses, .. otherAddresses];
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
