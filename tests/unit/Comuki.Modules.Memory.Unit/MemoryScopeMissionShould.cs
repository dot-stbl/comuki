using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Mission-scope wire-key round-trip. The Mission scope and its wire key
/// arrive together (#105 of the add-mission-cowork change); until a real
/// Mission entity lands (#96, currently blocked), the contract is:
/// - <c>MemoryScope.Mission = 4</c> round-trips through <c>MemoryScopeKeys</c>.
/// - <c>MemoryFactQuery</c> carries a strongly-typed <c>MissionId</c> for
///   the call site to identify the mission it wants to query.
/// </summary>
public sealed class MemoryScopeMissionShould
{
    [Fact(DisplayName = "Given MemoryScope.Mission, when the wire key is read, then it equals 'mission'")]
    public void MemoryScopeMissionKeyIsMission()
    {
        MemoryScopeKeys.Key(MemoryScope.Mission).ShouldBe("mission");
    }

    [Fact(DisplayName = "Given the wire key 'mission', when parsed, then MemoryScope.Mission comes back")]
    public void MemoryScopeKeyMissionParsesToMissionScope()
    {
        MemoryScopeKeys.Parse("mission").ShouldBe(MemoryScope.Mission);
    }

    [Fact(DisplayName = "Given a MemoryScope.Mission key, when ParseRequired is called, then MemoryScope.Mission comes back")]
    public void MemoryScopeKeyMissionParseRequiredReturnsMissionScope()
    {
        MemoryScopeKeys.ParseRequired("mission").ShouldBe(MemoryScope.Mission);
    }

    [Fact(DisplayName = "Given a query naming MissionId, when the query is constructed, then the field round-trips")]
    public void MissionIdRoundTripsThroughQuery()
    {
        var missionId = Guid.NewGuid();
        var query = new MemoryFactQuery(MissionId: missionId);

        query.MissionId.ShouldBe(missionId);
    }

    [Fact(DisplayName = "Given a query without MissionId, when the query is constructed, then MissionId defaults to null")]
    public void MissionIdDefaultsToNull()
    {
        var query = new MemoryFactQuery();

        query.MissionId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a query with Scope = Mission and MissionId, when the full query is constructed, then both fields stick")]
    public void MissionScopeAndMissionIdCoexist()
    {
        var missionId = Guid.NewGuid();
        var query = new MemoryFactQuery(
            Scope: MemoryScope.Mission,
            SubjectId: "stale-ignored",
            Kind: MemoryFactKind.Standing,
            Text: "hello world",
            Limit: 7,
            MissionId: missionId);

        query.Scope.ShouldBe(MemoryScope.Mission);
        query.MissionId.ShouldBe(missionId);
        query.Kind.ShouldBe(MemoryFactKind.Standing);
        query.Text.ShouldBe("hello world");
        query.Limit.ShouldBe(7);
    }
}
