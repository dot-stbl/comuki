using Comuki.Modules.Procedures.Domain.Catalog;

namespace Comuki.Modules.Procedures.Application.Ports;

/// <summary>
/// Port for loading a procedure-node-kind catalog from the control-plane
/// git. Lives in the Domain layer because the contract is pure — the
/// implementation that walks a folder is a detail. Consumers depend on
/// this surface; the catalog of the baseline v1 ships through an
/// in-process implementation.
/// </summary>
public interface INodeKindCatalogReader
{
    /// <summary>
    /// Loads the catalog from the named folder under the given control-plane
    /// root. Returns the parsed catalog; throws when a descriptor is
    /// structurally invalid or lacks an owner surface (spec scenario:
    /// "Descriptor missing an owner").
    /// </summary>
    /// <param name="controlPlaneRoot">Absolute path of the control-plane root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<NodeKindCatalog> LoadAsync(string controlPlaneRoot, CancellationToken cancellationToken = default);
}
