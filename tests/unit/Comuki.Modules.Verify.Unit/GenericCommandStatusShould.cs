using Comuki.Modules.Verify.Domain.Runs;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// <see cref="GenericCommandStatus"/> smart-type: wire round-trip,
/// <see cref="GenericCommandStatus.All"/>, and the guarded default.
/// </summary>
public sealed class GenericCommandStatusShould
{
    [Theory(DisplayName = "Given a known wire value, when FromWire is called, then it returns the matching status")]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("Green")]
    [InlineData("Red")]
    public void RoundTripKnownWireValues(string wire)
    {
        var status = GenericCommandStatus.FromWire(wire);

        status.Value.ShouldBe(wire);
        status.ToString().ShouldBe(wire);
    }

    [Fact(DisplayName = "Given an unknown wire value, when FromWire is called, then it throws")]
    public void ThrowOnUnknownWireValue()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => GenericCommandStatus.FromWire("Purple"));
    }

    [Fact(DisplayName = "Given the default struct value, when Value is read, then it resolves to Unspecified")]
    public void DefaultResolvesToUnspecified()
    {
        var status = default(GenericCommandStatus);

        status.Value.ShouldBe("Unspecified");
        status.ShouldBe(GenericCommandStatus.Unspecified);
    }

    [Fact(DisplayName = "Given All, when enumerated, then it contains exactly the four working statuses")]
    public void AllContainsExactlyFourStatuses()
    {
        GenericCommandStatus.All.ShouldBe(
        [
            GenericCommandStatus.Pending,
            GenericCommandStatus.Running,
            GenericCommandStatus.Green,
            GenericCommandStatus.Red,
        ]);
        GenericCommandStatus.All.ShouldNotContain(GenericCommandStatus.Unspecified);
    }
}
