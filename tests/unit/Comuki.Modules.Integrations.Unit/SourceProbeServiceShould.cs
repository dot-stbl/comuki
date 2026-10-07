using Comuki.Modules.Integrations.Application.Ports.Sources;
using Comuki.Modules.Integrations.Application.Sources;
using Comuki.Modules.Integrations.Application.Sync;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Integrations.Unit;

/// <summary>
/// Source probe service (issues #41-#42): exercises the reachable /
/// latency / provider-sentence projection against a mocked provider
/// registry. The provider implementation itself is a stub returning a
/// canned catalog page (no I/O), so the probe never crosses the wire.
/// </summary>
public sealed class SourceProbeServiceShould
{
    [Fact(DisplayName = "Given an unknown provider key, when ProbeDraft runs, then Reachable=false with a stable message")]
    public async Task ProbeRefusesUnknownProviderAsync()
    {
        var registry = new TicketProviderRegistry([], []);
        var service = new SourceProbeService(registry);

        var result = await service.ProbeDraftAsync("bitbucket", "{}", "HOOK_SECRET", TestContext.Current.CancellationToken);

        result.Reachable.ShouldBeFalse();
        result.Message.ShouldContain("not probeable");
    }

    [Fact(DisplayName = "Given the native provider, when ProbeDraft runs, then Reachable=false with a stable message")]
    public async Task ProbeRefusesNativeProviderAsync()
    {
        var registry = new TicketProviderRegistry([], []);
        var service = new SourceProbeService(registry);

        var result = await service.ProbeDraftAsync("native", "{}", "HOOK_SECRET", TestContext.Current.CancellationToken);

        result.Reachable.ShouldBeFalse();
        result.Message.ShouldContain("not probeable");
    }

    [Fact(DisplayName = "Given a registered provider with no providers registered, when ProbeDraft runs, then Reachable=false with a registered message")]
    public async Task ProbeRefusesUnregisteredProviderAsync()
    {
        var registry = new TicketProviderRegistry([], []);
        var service = new SourceProbeService(registry);

        var result = await service.ProbeDraftAsync("github", "{}", "HOOK_SECRET", TestContext.Current.CancellationToken);

        result.Reachable.ShouldBeFalse();
        result.Message.ShouldContain("not registered");
    }

    [Fact(DisplayName = "Given a registered provider returning one catalog page, when ProbeDraft runs, then Reachable=true with item count")]
    public async Task ProbeReachableProviderAsync()
    {
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        provider.FetchCatalogAsync(Arg.Any<SourceConnection>(), 1, Arg.Any<CancellationToken>())
            .Returns([
                InboundItem.Create(
                    ProjectId.New(),
                    TicketProvider.GitHub,
                    "acme/app#1",
                    "Issue #1",
                    "body",
                    "octocat",
                    "https://github.com/acme/app/issues/1",
                    "acme/app",
                    [],
                    InboundItemKind.Issue,
                    DateTimeOffset.UtcNow),
            ]);
        var registry = new TicketProviderRegistry([provider], []);
        var service = new SourceProbeService(registry);

        var result = await service.ProbeDraftAsync("github", "{}", "HOOK_SECRET", TestContext.Current.CancellationToken);

        result.Reachable.ShouldBeTrue();
        result.LatencyMs.ShouldBeGreaterThanOrEqualTo(0);
        result.Message.ShouldContain("reachable");
        result.Message.ShouldContain("1 item");
    }

    [Fact(DisplayName = "Given a registered provider that throws, when ProbeDraft runs, then Reachable=false with the exception type in the message")]
    public async Task ProbeUnreachableProviderAsync()
    {
        var provider = Substitute.For<ITicketSourceProvider>();
        provider.SourceKey.Returns(TicketProviderKeys.GitHub);
        provider.FetchCatalogAsync(Arg.Any<SourceConnection>(), 1, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<InboundItem>>(static _ => throw new HttpRequestException("503 Service Unavailable"));
        var registry = new TicketProviderRegistry([provider], []);
        var service = new SourceProbeService(registry);

        var result = await service.ProbeDraftAsync("github", "{}", "HOOK_SECRET", TestContext.Current.CancellationToken);

        result.Reachable.ShouldBeFalse();
        result.Message.ShouldContain("HttpRequestException");
    }
}
