using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Wire-key round-trip for <see cref="MemoryFactKind.BlackboardFinding" />.
/// The supersede behavior over the composite TopicKey is exercised through
/// the existing store tests; this file locks the kind/key contract.
/// </summary>
public sealed class MemoryFactKindBlackboardFindingShould
{
    [Fact(DisplayName = "Given MemoryFactKind.BlackboardFinding, when the wire key is read, then it equals 'blackboard-finding'")]
    public void BlackboardFindingKeyIsBlackboardFinding()
    {
        MemoryFactKindKeys.Key(MemoryFactKind.BlackboardFinding).ShouldBe("blackboard-finding");
    }

    [Fact(DisplayName = "Given the wire key 'blackboard-finding', when parsed, then MemoryFactKind.BlackboardFinding comes back")]
    public void BlackboardFindingKeyParsesToBlackboardFindingKind()
    {
        MemoryFactKindKeys.Parse("blackboard-finding").ShouldBe(MemoryFactKind.BlackboardFinding);
    }

    [Fact(DisplayName = "Given a BlackboardFinding key, when ParseRequired is called, then the kind comes back")]
    public void BlackboardFindingParseRequiredReturnsTheKind()
    {
        MemoryFactKindKeys.ParseRequired("blackboard-finding").ShouldBe(MemoryFactKind.BlackboardFinding);
    }

    [Fact(DisplayName = "Given an unknown wire key, when parsed, then the result is null rather than an exception")]
    public void UnknownKeyParsesToNull()
    {
        MemoryFactKindKeys.Parse("not-a-real-kind").ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown kind enum value, when the wire key is read, then ArgumentOutOfRangeException is thrown")]
    public void UnknownKindKeyThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => MemoryFactKindKeys.Key((MemoryFactKind)999));
    }

    [Fact(DisplayName = "Given an unknown wire key, when ParseRequired is called, then InvalidOperationException is thrown")]
    public void UnknownKeyParseRequiredThrows()
    {
        Should.Throw<InvalidOperationException>(static () => MemoryFactKindKeys.ParseRequired("not-a-real-kind"));
    }
}
