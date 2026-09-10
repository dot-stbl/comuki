namespace Comuki.Modules.Artifacts.Domain.VisualArtifacts;

/// <summary>Wire strings for <see cref="VisualArtifact.CreatedBy"/>.</summary>
public static class VisualArtifactCreatedByWire
{
    /// <summary>A worker container published the artifact.</summary>
    public const string Worker = "worker";

    /// <summary>The brain / host tool published the artifact.</summary>
    public const string Brain = "brain";
}
