using Comuki.Modules.Identity.Domain.Scopes;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>Wire keys for <see cref="ScopeLevel"/> used by EF converters.</summary>
public sealed class ScopeLevelKeysShould
{
    [Theory(DisplayName = "Given a scope level, when Key then Parse, then the level round-trips")]
    [InlineData(ScopeLevel.Platform, ScopeLevelKeys.Platform)]
    [InlineData(ScopeLevel.Project, ScopeLevelKeys.Project)]
    public void RoundTrip(ScopeLevel level, string key)
    {
        ScopeLevelKeys.Key(level).ShouldBe(key);
        ScopeLevelKeys.Parse(key).ShouldBe(level);
    }

    [Fact(DisplayName = "Given an unknown key or out-of-range level, when parsed/keyed, then null or throw")]
    public void RefuseUnknown()
    {
        ScopeLevelKeys.Parse("org").ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(static () => ScopeLevelKeys.Key((ScopeLevel)99));
    }
}
