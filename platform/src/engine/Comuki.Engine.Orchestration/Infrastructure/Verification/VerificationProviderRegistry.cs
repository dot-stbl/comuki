using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Shared.Contracts.Verification;

namespace Comuki.Engine.Orchestration.Infrastructure.Verification;

/// <summary>
/// Runtime side of the gate-provider registry
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c> Requirement
/// "Gate-provider registry is an open SPI"). The host wires every
/// provider with <c>AddVerificationGateProvider&lt;T&gt;()</c> and DI
/// hands the registry a singleton <c>IEnumerable&lt;IVerificationGateProvider&gt;</c>;
/// <see cref="Snapshot"/> freezes the registration order so the
/// evaluation phase iterates providers deterministically.
/// </summary>
public sealed class VerificationProviderRegistry : IVerificationProviderRegistry
{
    private readonly IReadOnlyList<IVerificationGateProvider> snapshot;

    /// <summary>
    /// Construction-time duplicate guard — two providers claiming the
    /// same <see cref="IVerificationGateProvider.GateName"/> would race
    /// on the same (work_item_id, gate_name) row and the re-evaluation
    /// contract ("Per-gate uniqueness" in the spec) becomes
    /// non-deterministic. We throw at composition so the host sees the
    /// problem on the first boot, never silently in production.
    /// </summary>
    public VerificationProviderRegistry(IEnumerable<IVerificationGateProvider> providers)
    {
        var resolved = providers.ToArray();
        var seen = new Dictionary<string, IVerificationGateProvider>(StringComparer.Ordinal);
        foreach (var provider in resolved)
        {
            if (!seen.TryAdd(provider.GateName, provider))
            {
                throw new InvalidOperationException(
                    $"Verification gate '{provider.GateName}' is registered by two providers "
                    + $"({seen[provider.GateName].GetType().FullName} and {provider.GetType().FullName}). "
                    + "Each IVerificationGateProvider must declare a unique GateName — the (work_item_id, "
                    + "gate_name) row key has no second dimension to disambiguate on.");
            }
        }

        snapshot = resolved;
    }

    /// <inheritdoc />
    public IReadOnlyList<IVerificationGateProvider> Snapshot()
    {
        return snapshot;
    }
}
