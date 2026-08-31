namespace Comuki.Engine.Compute.Scaling;

/// <summary>Desired pool change for one profile of a project.</summary>
/// <param name="StartWorkers">How many workers to start now (0 — nothing to add).</param>
/// <param name="StopIdleWorkers">How many stale idle workers to stop, oldest first (0 — nothing to reap).</param>
public sealed record ScaleDecision(int StartWorkers, int StopIdleWorkers);
