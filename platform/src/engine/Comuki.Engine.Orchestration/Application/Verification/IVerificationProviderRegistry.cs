using Comuki.Shared.Contracts.Verification;

namespace Comuki.Engine.Orchestration.Application.Verification;

/// <summary>
/// Typed registry of <see cref="IVerificationGateProvider"/>
/// implementations (add-orchestra §3 — Coda,
/// <c>verification/spec.md</c> Requirement "Gate-provider registry is
/// an open SPI"). The host calls
/// <c>AddVerificationGateProvider&lt;T&gt;()</c> in the composition
/// root; the runtime side holds the resolved <c>IEnumerable</c> and
/// the host iterates it when a work item enters the verification
/// phase. The registry is a singleton — providers are constructed
/// through DI (the host wires them with their own dependencies), so
/// the registry itself is a thin index, not a factory.
/// </summary>
public interface IVerificationProviderRegistry
{
    /// <summary>Snapshot of every registered provider, in registration order (the order the host wired them).</summary>
    public IReadOnlyList<IVerificationGateProvider> Snapshot();
}
