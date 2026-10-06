using Comuki.Engine.Orchestration.Infrastructure.Verification;
using Comuki.Shared.Contracts.Verification;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// <see cref="VerificationProviderRegistry"/>: the duplicate
/// <c>GateName</c> guard at composition time. Two providers claiming
/// the same <see cref="IVerificationGateProvider.GateName"/> would race
/// on the same <c>(work_item_id, gate_name)</c> row, and the
/// re-evaluation contract ("Per-gate uniqueness" in the spec) becomes
/// non-deterministic. The registry must throw at composition so the
/// host sees the problem on the first boot, never silently in
/// production — the message names both providers' type identities so
/// the operator can locate the second registration without grepping
/// the host composition root.
/// </summary>
public sealed class VerificationProviderRegistryShould
{
    [Fact(DisplayName = "Given two providers with the same gate name, when the registry is constructed, then it throws InvalidOperationException naming both providers")]
    public void ThrowsWhenTwoProvidersClaimTheSameGateName()
    {
        var first = new StubProvider("verify:shared-name");
        var second = new StubProvider("verify:shared-name");

        var exception = Should.Throw<InvalidOperationException>(
            () => new VerificationProviderRegistry([first, second]));

        exception.Message.ShouldContain("verify:shared-name");
        exception.Message.ShouldContain(typeof(StubProvider).FullName!);
    }

    [Fact(DisplayName = "Given providers with distinct gate names, when the registry is constructed, then it snapshots the providers in registration order")]
    public void SnapshotsInRegistrationOrderWhenGateNamesAreDistinct()
    {
        var a = new StubProvider("verify:first");
        var b = new StubProvider("verify:second");
        var c = new StubProvider("verify:third");

        var registry = new VerificationProviderRegistry([a, b, c]);

        registry.Snapshot().Select(static provider => provider.GateName).ShouldBe(
            ["verify:first", "verify:second", "verify:third"],
            ignoreOrder: false);
    }

    [Fact(DisplayName = "Given an empty provider set, when the registry is constructed, then Snapshot returns an empty list")]
    public void EmptyProvidersSnapshotIsEmpty()
    {
        var registry = new VerificationProviderRegistry([]);

        registry.Snapshot().ShouldBeEmpty();
    }

    private sealed class StubProvider(string gateName) : IVerificationGateProvider
    {
        public string GateName { get; } = gateName;

        public bool AppliesTo(VerificationContext context)
        {
            return true;
        }

        public Task<GateVerdictResult> EvaluateAsync(
            VerificationContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GateVerdictResult(GateVerdict.Passed, [], GateName));
        }
    }
}
