namespace Comuki.Engine.Compute.Environments.Catalog;

/// <summary>
/// In-process read-only catalog of environment classes the fleet
/// allowlists. A future slice (task 6.2) wires a fleet-allowlist gate
/// here; for now the catalog is open and exposes every seeded bundle to
/// every reader. <c>TryGet</c> returns <c>false</c> on miss so callers
/// map a missing class to a typed refusal without exception plumbing.
/// </summary>
public interface IEnvironmentCatalog
{
    /// <summary>Looks up a class by its stable id (case-sensitive).</summary>
    /// <param name="id">Catalog id (e.g. <c>"net10-sdk-bun"</c>).</param>
    /// <param name="bundle">Resolved bundle when the id is present; <c>null</c> otherwise.</param>
    /// <returns>True when a bundle was found.</returns>
    public bool TryGet(string id, out EnvironmentBundle? bundle);

    /// <summary>Lists every catalog entry — the operator surface (worker-environments spec §"Bundle catalog is readable").</summary>
    /// <returns>Sorted-by-id snapshot of the catalog.</returns>
    public IReadOnlyList<EnvironmentBundle> List();

    /// <summary>
    /// Reports whether the supplied publisher name is on the fleet
    /// allowlist (add-worker-environments 6.2; spec scenario
    /// "Unallowlisted community bundle is not started"). The default
    /// catalog ships with the Comuki shelf only — community and org
    /// publishers are refused until the operator adds them.
    /// </summary>
    /// <param name="publisher">Wire-form publisher name (e.g. <see cref="Shape.EnvironmentPublisher.Value" />).</param>
    /// <returns>True when the publisher is on the allowlist; false otherwise (including null/empty input).</returns>
    public bool IsAllowed(string publisher);
}
