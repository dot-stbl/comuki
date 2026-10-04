namespace Comuki.Modules.Procedures.Application;

/// <summary>
/// Application-layer options for the procedures module. Bound from the
/// same <c>ControlPlane</c> section the host's
/// <c>ControlPlaneCatalogOptions</c> already binds — both readers see one
/// <c>ControlPlane:Root</c> config key, so the host's existing validation
/// pass covers this options class as well.
/// </summary>
public sealed class ProceduresOptions
{
    /// <summary>Config section this options class binds from.</summary>
    public const string SectionName = "ControlPlane";

    /// <summary>
    /// Absolute path of the control-plane root. The catalog reader
    /// (<c>INodeKindCatalogReader.LoadAsync</c>) appends the kind-folder
    /// name; the absolute path is mandatory because the host runs from a
    /// bin directory and a relative <c>control-plane/</c> would miss the
    /// folder. Tests and crown-e2e fixtures set this explicitly to a
    /// temp dir or the repo's checked-in <c>control-plane/</c>.
    /// </summary>
    public string ControlPlaneRoot { get; init; } = string.Empty;
}
