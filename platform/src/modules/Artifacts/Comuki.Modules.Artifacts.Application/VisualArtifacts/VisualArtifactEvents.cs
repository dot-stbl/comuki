namespace Comuki.Modules.Artifacts.Application.VisualArtifacts;

/// <summary>
/// Run-journal event types the visual-artifacts surface emits.
/// Concatenated with the run id by <see cref="RunJournalPayloads"/>.
/// </summary>
public static class VisualArtifactEvents
{
    /// <summary>Emitted by <see cref="VisualArtifactService"/> when an artifact has been written.</summary>
    public const string Published = "artifact.published";
}
