namespace Comuki.Platform.Orchestration.Interfaces;

/// <summary>
/// Marker contract for the orchestration feature — the heart of Comuki's
/// control plane. Filled in during Phase 3 (Slice 0) when the claim/lease
/// loop and DAG engine land. See <c>.soly/docs/comuki-architecture.md</c> § 03.
/// </summary>
public interface IOrchestrationService
{
}

/// <summary>
/// Placeholder implementation of <see cref="IOrchestrationService"/>.
/// Replaced with the real orchestration in Phase 3 (Slice 0).
/// </summary>
public sealed class NoOpOrchestrationService : IOrchestrationService
{
}
