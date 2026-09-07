using Comuki.Modules.Identity.Domain.Subjects;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>Wire keys for <see cref="SubjectType"/> used by EF converters.</summary>
public sealed class SubjectTypeKeysShould
{
    [Theory(DisplayName = "Given a subject type, when Key then Parse, then the value round-trips")]
    [InlineData(SubjectType.User, SubjectTypeKeys.User)]
    [InlineData(SubjectType.ApiKey, SubjectTypeKeys.ApiKey)]
    public void RoundTrip(SubjectType type, string key)
    {
        SubjectTypeKeys.Key(type).ShouldBe(key);
        SubjectTypeKeys.Parse(key).ShouldBe(type);
    }

    [Fact(DisplayName = "Given a null key or unknown key, when parsed, then null is returned")]
    public void RefuseUnknown()
    {
        SubjectTypeKeys.Parse(null).ShouldBeNull();
        SubjectTypeKeys.Parse("machine").ShouldBeNull();
        SubjectTypeKeys.Parse(string.Empty).ShouldBeNull();
    }
}
