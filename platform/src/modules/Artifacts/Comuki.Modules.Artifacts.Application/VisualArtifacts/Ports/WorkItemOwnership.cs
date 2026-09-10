using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;

/// <summary>The result of a successful ownership probe — the parent run + project of a leased work item.</summary>
/// <param name="ProjectId">Project the run belongs to (denormalised for the visual-artifact row).</param>
/// <param name="RunId">Run the work item is part of.</param>
public sealed record WorkItemOwnership(ProjectId ProjectId, RunId RunId);
