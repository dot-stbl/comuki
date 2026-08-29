namespace Comuki.Platform.Database.Runs.Contexts;

/// <summary>
/// DbContext for runs, stages, tasks, and the append-only event log.
/// EF Core wiring (base class, <c>OnModelCreating</c>, <c>FOR UPDATE SKIP LOCKED</c>
/// claim query) lands in Phase 3.2 (Slice 0 Step 1) when the <c>runs</c>
/// table is created. This is a placeholder type that keeps the project graph
/// wired so the solution compiles; intentionally NOT inheriting <c>DbContext</c>
/// yet because the EF Core package is not referenced.
/// </summary>
public sealed class RunsDbContext
{
    /// <summary>
    /// Marker constructor for future DI registration. Will accept
    /// <c>DbContextOptions&lt;RunsDbContext&gt;</c> once EF Core lands.
    /// </summary>
    public RunsDbContext()
    {
    }
}
