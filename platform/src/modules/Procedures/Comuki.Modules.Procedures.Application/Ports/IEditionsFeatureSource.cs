using Comuki.Modules.Procedures.Domain.Editions;

namespace Comuki.Modules.Procedures.Application.Ports;

/// <summary>
/// Port the compile gate (task 2.3) consults to learn which editions
/// feature keys the effective edition grants. Domain logic depends only
/// on the resolved <see cref="GrantedFeatureKeys"/>; the host wires the
/// concrete implementation when the license/effective-edition policy
/// is in scope.
/// 
/// <para>
/// Resolved at compile time, never mid-run. A pinned version stays
/// executable under the edition that compiled it even after a downgrade
/// (spec scenario: "Paid kind inside an active pin after downgrade"):
/// once a run is executing, the gate does not run again on the kind.
/// </para>
/// </summary>
public interface IEditionsFeatureSource
{
    /// <summary>
    /// Returns the editions feature keys the effective edition grants at
    /// the moment of the call. The host implementation is expected to
    /// cache per publication pass — calling per-kind would re-decode the
    /// edition on every node, which the gate does not do.
    /// </summary>
    public GrantedFeatureKeys ResolveEffective();
}
