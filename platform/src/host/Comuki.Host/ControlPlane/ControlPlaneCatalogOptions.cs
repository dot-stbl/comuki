namespace Comuki.Host.ControlPlane;

/// <summary>
/// Options for the control-plane content catalog. Bound from the
/// <c>ControlPlane</c> configuration section.
/// </summary>
public sealed class ControlPlaneCatalogOptions()
{
    public const string SectionName = "ControlPlane";

    /// <summary>
    /// Explicit control-plane root directory (compose: a mounted path,
    /// tests: a temp dir). When null, the catalog probes upward from the
    /// application base directory for a directory named
    /// <see cref="ControlPlaneCatalog.RootFolderName"/>.
    /// </summary>
    public string? Root { get; init; }
}
