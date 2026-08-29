namespace Comuki.Platform.Api.Contracts;

/// <summary>
/// Brief handed to a worker on a stage. The full schema (git-ref, role, model
/// routing key, scoped rules, snapshot pointer) lands in Phase 3.3 (Slice 0
/// Step 2) when the Translator starts producing real briefs. This is a
/// placeholder type that keeps the project graph wired so the solution compiles.
/// </summary>
public sealed record BriefRequest
{
    /// <summary>
    /// Opaque task identifier — the contract the rest of the run is keyed on.
    /// </summary>
    public required string TaskKey { get; init; }
}
