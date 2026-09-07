using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;

/// <summary>
/// One sweep of the escalation-timeout reaper: how many runs the sweep
/// transitioned, and which ones. Mirrors the shape of
/// <c>ReapedLease</c>: a small immutable record returned to the hosted
/// worker so it can log a single structured line.
/// </summary>
/// <param name="Archived"></param>
/// <param name="RunIds"></param>
public sealed record EscalationTimeoutSwept(int Archived, IReadOnlyList<RunId> RunIds);
