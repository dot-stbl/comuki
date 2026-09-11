using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Chat.Brain;

/// <summary>
/// Where the orchestrator finds the brain host. The same <c>[brain]</c>
/// section the brain host reads for its own listener: the brain declares
/// <c>grpcPort</c>, the orchestrator declares the <c>endpoint</c> it dials
/// (<c>COMUKI_BRAIN_ENDPOINT</c> on the deployment side). An absent
/// endpoint is not a misconfiguration — it selects the in-process
/// <see cref="BrainStub"/>, so a single-container install boots without a
/// brain.
/// </summary>
public sealed class BrainClientOptions
{
    /// <summary>Config section name (shared with the brain host's own options).</summary>
    public const string SectionName = "brain";

    /// <summary>Key of <see cref="Endpoint"/> inside the section.</summary>
    public const string EndpointKey = "endpoint";

    /// <summary>gRPC address of the brain host, e.g. <c>http://comuki-brain:17004</c>.</summary>
    [Required]
    [Url]
    public required string Endpoint { get; init; }
}
