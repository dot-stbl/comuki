namespace Comuki.Host.Artifacts.Models;

/// <summary>
/// Page envelope returned by the visual-artifacts list endpoint.
/// </summary>
/// <param name="Items">Artifacts, oldest first. Empty when the project has none.</param>
/// <param name="ProjectId">Project id echoed back to the caller.</param>
public sealed record VisualArtifactPage(
    IReadOnlyList<VisualArtifactListItem> Items,
    Guid ProjectId);
