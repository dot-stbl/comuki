namespace Comuki.Shared.Contracts.Compute;

/// <summary>Coarse capacity snapshot for scale decisions.</summary>
/// <param name="FreeSlots"></param>
/// <param name="RunningWorkers"></param>
public sealed record ComputeCapacity(int FreeSlots, int RunningWorkers);
