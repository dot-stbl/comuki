using Comuki.Engine.Compute.Exceptions;
using Comuki.Engine.Compute.Options;
using Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace Comuki.Engine.Compute.Providers;

/// <summary>
/// Verifies the Docker egress fence before a worker start: the configured
/// fenced network (<see cref="DockerComputeOptions.FencedNetwork"/>) must
/// exist and be internal. Fail-closed — a missing configuration, a missing
/// network or a non-internal network throws
/// <see cref="ComputeFenceException"/> unless
/// <c>Compute:AllowUnfencedEgress=true</c> (development override; the
/// unfenced start is logged as a warning so it stays observable).
/// </summary>
/// <param name="networks">Docker network operations (inspect only).</param>
/// <param name="logger">Fence-scoped logger; surfaces unfenced-override starts.</param>
public sealed class DockerEgressFence(
    INetworkOperations networks,
    ILogger<DockerEgressFence> logger)
{
    /// <summary>
    /// Resolves the network mode for the worker container: the verified
    /// fenced network, or the plain <see cref="DockerComputeOptions.NetworkMode"/>
    /// under the unfenced dev override.
    /// </summary>
    /// <param name="options">Docker compute options (fenced network + fallback network mode).</param>
    /// <param name="allowUnfenced">Value of <c>Compute:AllowUnfencedEgress</c>.</param>
    /// <param name="cancellationToken">Cancels the Docker network inspect only; once the inspect returns, the fence verdict is synchronous.</param>
    /// <returns>The Docker network the worker container joins.</returns>
    public async Task<string> ResolveNetworkModeAsync(
        DockerComputeOptions options,
        bool allowUnfenced,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.FencedNetwork))
        {
            if (allowUnfenced)
            {
                logger.LogWarning(
                    "Worker starts unfenced: Compute:Docker:FencedNetwork is not configured (Compute:AllowUnfencedEgress=true)");
                return options.NetworkMode;
            }

            throw new ComputeFenceException(
                "Compute:Docker:FencedNetwork is not configured; refusing to start a worker without an egress fence. "
                    + "Point it at an internal Docker network (deploy/compose defines comuki-worker-net) "
                    + "or set Compute:AllowUnfencedEgress=true for development.");
        }

        try
        {
            if (await networks.InspectNetworkAsync(options.FencedNetwork, cancellationToken) is { Internal: true })
            {
                return options.FencedNetwork;
            }

            if (allowUnfenced)
            {
                logger.LogWarning(
                    "Worker starts unfenced: Docker network {FencedNetwork} exists but is not internal (Compute:AllowUnfencedEgress=true)",
                    options.FencedNetwork);
                return options.NetworkMode;
            }

            throw new ComputeFenceException(
                $"Docker network '{options.FencedNetwork}' is not internal; refusing to start a worker on a network "
                    + "with a default route to the internet. Recreate the network with internal:true "
                    + "or set Compute:AllowUnfencedEgress=true for development.");
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException)
        {
            if (allowUnfenced)
            {
                logger.LogWarning(
                    exception,
                    "Worker starts unfenced: Docker network {FencedNetwork} could not be inspected (Compute:AllowUnfencedEgress=true)",
                    options.FencedNetwork);
                return options.NetworkMode;
            }

            throw new ComputeFenceException(
                $"Docker network '{options.FencedNetwork}' could not be inspected; refusing to start a worker "
                    + "without a verified egress fence. Create the network or set Compute:AllowUnfencedEgress=true for development.",
                exception);
        }
    }
}
