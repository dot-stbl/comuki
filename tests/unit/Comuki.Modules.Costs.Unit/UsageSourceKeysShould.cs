using Comuki.Modules.Costs.Domain.Events;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Wire-key round-trip for <see cref="UsageSourceKeys"/>.</summary>
public sealed class UsageSourceKeysShould
{
    [Theory(DisplayName = "Given a usage source, when Of is called, then the stable key is returned")]
    [InlineData(UsageSource.Proxy, UsageSourceKeys.Proxy)]
    [InlineData(UsageSource.Brain, UsageSourceKeys.Brain)]
    [InlineData(UsageSource.Worker, UsageSourceKeys.Worker)]
    [InlineData(UsageSource.System, UsageSourceKeys.System)]
    public void MapSourceToKey(UsageSource source, string expected)
    {
        UsageSourceKeys.Of(source).ShouldBe(expected);
    }

    [Theory(DisplayName = "Given a known key, when Parse is called, then the matching source is returned")]
    [InlineData(UsageSourceKeys.Proxy, UsageSource.Proxy)]
    [InlineData(UsageSourceKeys.Brain, UsageSource.Brain)]
    [InlineData(UsageSourceKeys.Worker, UsageSource.Worker)]
    [InlineData(UsageSourceKeys.System, UsageSource.System)]
    public void ParseKnownKey(string key, UsageSource expected)
    {
        UsageSourceKeys.Parse(key).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given an unknown key, when Parse is called, then throws")]
    public void RefuseUnknownKey()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => UsageSourceKeys.Parse("unknown"));
    }

    [Fact(DisplayName = "Given an undefined enum value, when Of is called, then throws")]
    public void RefuseUndefinedSource()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => UsageSourceKeys.Of((UsageSource)99));
    }
}
