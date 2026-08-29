namespace Comuki.Platform.Entity.Core;

/// <summary>
/// A single orchestration run — one ticket end-to-end through the platform.
/// Schema (status, stages, lease, timestamps) lands in Phase 3.1 (Slice 0 Step 1)
/// when the Postgres <c>runs</c> table is created. This is a placeholder type that
/// keeps the project graph wired so the solution compiles.
/// </summary>
public sealed record Run
{
    /// <summary>
    /// Server-assigned unique identifier.
    /// </summary>
    public Guid Id { get; init; }
}
