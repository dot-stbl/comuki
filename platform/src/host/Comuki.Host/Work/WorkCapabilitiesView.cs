using System.Text.Json.Serialization;
using Comuki.Modules.Work.Application.Ports.Capabilities;

namespace Comuki.Host.Work;

/// <summary>
/// Capability catalogue response — the static <see cref="WorkCapabilities"/>
/// enumerated for the Capability Broker (the umbrella's #90 work).
/// Read-only, permission work:read.
/// </summary>
public sealed record WorkCapabilitiesView(
    [property: JsonPropertyName("capabilities")] IReadOnlyList<WorkCapabilities.WorkCapabilityDescriptor> Capabilities)
{
    /// <summary>Snapshot the Work module's static capability catalogue.</summary>
    public static WorkCapabilitiesView Create()
    {
        return new(WorkCapabilities.All);
    }
}
