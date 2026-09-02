using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Application.Sync;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Registry lookup by source key — first registration wins, misses return null.</summary>
public sealed class TicketProviderRegistryShould
{
    [Fact(DisplayName = "Given registered sources and sync ports, when Find is called, then the matching ones are returned")]
    public void ResolveRegisteredProviders()
    {
        var github = Substitute.For<ITicketSourceProvider>();
        github.SourceKey.Returns("github");
        var jira = Substitute.For<ITicketSourceProvider>();
        jira.SourceKey.Returns("jira");
        var sync = Substitute.For<ITicketSyncPort>();
        sync.SourceKey.Returns("github");
        var registry = new TicketProviderRegistry([github, jira], [sync]);

        registry.FindSource("github").ShouldBeSameAs(github);
        registry.FindSource("jira").ShouldBeSameAs(jira);
        registry.FindSync("github").ShouldBeSameAs(sync);
        registry.FindSource("native").ShouldBeNull();
        registry.FindSync("jira").ShouldBeNull();
    }

    [Fact(DisplayName = "Given two sources with the same key, when FindSource is called, then the first registration wins")]
    public void PreferFirstRegistration()
    {
        var first = Substitute.For<ITicketSourceProvider>();
        first.SourceKey.Returns("github");
        var second = Substitute.For<ITicketSourceProvider>();
        second.SourceKey.Returns("github");
        var registry = new TicketProviderRegistry([first, second], []);

        registry.FindSource("github").ShouldBeSameAs(first);
    }
}
