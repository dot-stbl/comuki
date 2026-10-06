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
/// <param name="providers">DI-resolved sequence of registered providers, in registration order.</param>
public sealed class VerificationProviderRegistry(
    IEnumerable<IVerificationGateProvider> providers) : IVerificationProviderRegistry
{
    private readonly IReadOnlyList<IVerificationGateProvider> snapshot = [.. providers];

    /// <inheritdoc />
    public IReadOnlyList<IVerificationGateProvider> Snapshot()
    {
        return snapshot;
    }
}
