using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Contracts.Usage;

namespace Comuki.Modules.Proxy.Application.Budgeting;

/// <summary>
/// Default budget enforcer: sums the project's <c>proxy</c>-source spend
/// for the current calendar month and compares against the key's
/// <see cref="VirtualKey.BudgetUsd"/>. The cost filter keeps brain / worker
/// spend from leaking into the proxy verdict — a hard project cap belongs to
/// the costs module, not to the proxy pre-flight.
/// </summary>
/// <param name="store">Costs store (proxy source only).</param>
/// <param name="clock">Wall-clock for the month boundary.</param>
public sealed class DefaultProxyBudgetEnforcer(IUsageEventStore store, TimeProvider clock) : IProxyBudgetEnforcer
{
    /// <inheritdoc />
    public async Task<ProxyBudgetVerdict> EvaluateAsync(VirtualKey key, CancellationToken cancellationToken = default)
    {
        if (key.BudgetUsd is not { } budgetUsd)
        {
            return new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: null, SpentUsdMicros: 0, RetryAfterSeconds: 0);
        }

        var monthStart = ProxyBudgetMath.StartOfMonth(clock.GetUtcNow());
        var spentUsdMicros = await store.SumProjectCostBySourceAsync(
            key.ProjectId,
            UsageSources.Proxy,
            monthStart,
            cancellationToken);
        var capUsdMicros = ProxyBudgetMath.ToMicros(budgetUsd);

        if (spentUsdMicros < capUsdMicros)
        {
            return new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: capUsdMicros, SpentUsdMicros: spentUsdMicros, RetryAfterSeconds: 0);
        }

        var retryAfter = ProxyBudgetMath.SecondsUntilNextMonth(clock.GetUtcNow());
        return new ProxyBudgetVerdict(Allowed: false, CapUsdMicros: capUsdMicros, SpentUsdMicros: spentUsdMicros, RetryAfterSeconds: retryAfter);
    }
}
