using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Costs;

/// <summary>
/// Read port for soft/hard project budgets (USD micros). Backed by
/// <c>ProjectSettings</c> through a host adapter — Costs does not reference
/// the Projects module.
/// </summary>
public interface IProjectBudgetSettings
{
    /// <summary>Current budget caps for the project; null limits mean "no cap".</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    public Task<ProjectBudgetCaps> GetAsync(ProjectId projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Soft / hard USD micros caps. Soft is advisory (attention); hard triggers
/// <see cref="IBudgetGate.HardStopAsync"/>. Null = unlimited.
/// </summary>
/// <param name="SoftLimitUsdMicros"></param>
/// <param name="HardLimitUsdMicros"></param>
public sealed record ProjectBudgetCaps(long? SoftLimitUsdMicros, long? HardLimitUsdMicros);
