using Comuki.Modules.Intake.Domain.Tickets;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Wire keys for <see cref="TicketProvider"/> — webhook route segments and EF converters.</summary>
public sealed class TicketProviderKeysShould
{
    [Theory(DisplayName = "Given a ticket provider, when Key then TryParse, then the provider round-trips")]
    [InlineData(TicketProvider.GitHub, TicketProviderKeys.GitHub)]
    [InlineData(TicketProvider.GitLab, TicketProviderKeys.GitLab)]
    [InlineData(TicketProvider.YandexTracker, TicketProviderKeys.YandexTracker)]
    [InlineData(TicketProvider.Jira, TicketProviderKeys.Jira)]
    [InlineData(TicketProvider.Native, TicketProviderKeys.Native)]
    public void RoundTrip(TicketProvider provider, string key)
    {
        TicketProviderKeys.Key(provider).ShouldBe(key);
        TicketProviderKeys.TryParse(key).ShouldBe(provider);
        TicketProviderKeys.All.ShouldContain(key);
    }

    [Fact(DisplayName = "Given an unknown key or out-of-range provider, when parsed/keyed, then null or throw")]
    public void RefuseUnknown()
    {
        TicketProviderKeys.TryParse(null).ShouldBeNull();
        TicketProviderKeys.TryParse("linear").ShouldBeNull();
        TicketProviderKeys.All.Count.ShouldBe(5);
        Should.Throw<ArgumentOutOfRangeException>(static () => TicketProviderKeys.Key((TicketProvider)99));
    }
}
