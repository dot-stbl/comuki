using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Repositories.Unit;

/// <summary>
/// Pure lattice meet on <see cref="RepositoryAccess"/> — the operation
/// behind <c>effective = min(attachment.access, credential.DefaultAccess)</c>.
/// Covers every legal pair plus the two "never escalates" scenarios from
/// the spec (a Read attachment through a Write-capable credential stays
/// Read; an External attachment never resolves a credential).
/// </summary>
public sealed class RepositoryAccessShould
{
    private readonly DateTimeOffset now = new(2026, 9, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a default value, when accessed, then the placeholder is Unspecified")]
    public void DefaultIsUnspecified()
    {
        var access = default(RepositoryAccess);

        access.ShouldBe(RepositoryAccess.Unspecified);
        access.Value.ShouldBe("Unspecified");
        access.ToString().ShouldBe("Unspecified");
    }

    [Theory(DisplayName = "Given two access levels, when Min is called, then the more restrictive one wins")]
    [InlineData(nameof(RepositoryAccess.Read), nameof(RepositoryAccess.Write), nameof(RepositoryAccess.Read))]
    [InlineData(nameof(RepositoryAccess.Write), nameof(RepositoryAccess.Write), nameof(RepositoryAccess.Write))]
    [InlineData(nameof(RepositoryAccess.Read), nameof(RepositoryAccess.Read), nameof(RepositoryAccess.Read))]
    [InlineData(nameof(RepositoryAccess.External), nameof(RepositoryAccess.Write), nameof(RepositoryAccess.External))]
    [InlineData(nameof(RepositoryAccess.External), nameof(RepositoryAccess.Read), nameof(RepositoryAccess.External))]
    [InlineData(nameof(RepositoryAccess.Write), nameof(RepositoryAccess.Read), nameof(RepositoryAccess.Read))]
    [InlineData(nameof(RepositoryAccess.Write), nameof(RepositoryAccess.External), nameof(RepositoryAccess.External))]
    [InlineData(nameof(RepositoryAccess.Read), nameof(RepositoryAccess.External), nameof(RepositoryAccess.External))]
    public void MinReturnsTheMoreRestrictive(string leftWire, string rightWire, string expectedWire)
    {
        var left = RepositoryAccess.FromWire(leftWire);
        var right = RepositoryAccess.FromWire(rightWire);
        var expected = RepositoryAccess.FromWire(expectedWire);

        RepositoryAccess.Min(left, right).ShouldBe(expected);
        RepositoryAccess.Min(right, left).ShouldBe(expected);
    }

    [Theory(DisplayName = "Given the wire forms of every working access level, when FromWire is called, then the same smart-type is returned")]
    [InlineData(nameof(RepositoryAccess.External))]
    [InlineData(nameof(RepositoryAccess.Read))]
    [InlineData(nameof(RepositoryAccess.Write))]
    public void FromWireRoundTripsWorkingLevels(string wire)
    {
        var access = RepositoryAccess.FromWire(wire);

        access.Value.ShouldBe(wire);
        access.ToString().ShouldBe(wire);
    }

    [Fact(DisplayName = "Given an unknown wire form, when FromWire is called, then it throws ArgumentOutOfRangeException")]
    public void FromWireThrowsOnUnknownValue()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => RepositoryAccess.FromWire("NotALevel"));
    }

    [Fact(DisplayName = "Given the closed access set, when All is enumerated, then it lists External, Read, Write in order")]
    public void AllContainsWorkingLevelsInLatticeOrder()
    {
        RepositoryAccess.All.ShouldBe(
        [
            RepositoryAccess.External,
            RepositoryAccess.Read,
            RepositoryAccess.Write,
        ]);
    }

    [Fact(DisplayName = "Given a credential with DefaultAccess Write, when EffectiveAccess is asked with a Read attachment, then it returns Read")]
    public void ReadAttachmentCannotEscalateThroughWriteCapableCredential()
    {
        var credential = RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration-write", RepositoryAccess.Write, now);

        credential.EffectiveAccess(RepositoryAccess.Read).ShouldBe(RepositoryAccess.Read);
    }

    [Fact(DisplayName = "Given a credential with DefaultAccess Read, when EffectiveAccess is asked with an External attachment, then it returns External")]
    public void ExternalAttachmentNeverResolvesACredential()
    {
        var credential = RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration-read", RepositoryAccess.Read, now);

        credential.EffectiveAccess(RepositoryAccess.External).ShouldBe(RepositoryAccess.External);
        credential.EffectiveAccess(RepositoryAccess.Write).ShouldBe(RepositoryAccess.Read);
    }

    private static RepositoryId RepositoryIdForTest()
    {
        return RepositoryId.New();
    }
}
