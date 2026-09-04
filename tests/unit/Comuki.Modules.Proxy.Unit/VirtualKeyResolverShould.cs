using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Modules.Proxy.Application.Resolving;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Lifecycle policy of <see cref="VirtualKeyResolver"/>: presence, expiry, allowed-models.</summary>
public sealed class VirtualKeyResolverShould
{
    [Fact(DisplayName = "Given a missing bearer, when ResolveAsync runs, then outcome is Missing")]
    public async Task MissingBearerReturnsMissingAsync()
    {
        var resolver = new VirtualKeyResolver(Substitute.For<IVirtualKeyStore>(), TimeProvider.System);

        var resolution = await resolver.ResolveAsync(token: null, requestedModel: null, TestContext.Current.CancellationToken);

        resolution.Outcome.ShouldBe(VirtualKeyResolver.ResolveOutcome.Missing);
        resolution.Key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an unknown token, when ResolveAsync runs, then outcome is Missing")]
    public async Task UnknownTokenReturnsMissingAsync()
    {
        var store = Substitute.For<IVirtualKeyStore>();
        _ = store.FindAsync("vkey_unknown", Arg.Any<CancellationToken>()).Returns((VirtualKey?)null);
        var resolver = new VirtualKeyResolver(store, TimeProvider.System);

        var resolution = await resolver.ResolveAsync(token: "vkey_unknown", requestedModel: null, TestContext.Current.CancellationToken);

        resolution.Outcome.ShouldBe(VirtualKeyResolver.ResolveOutcome.Missing);
    }

    [Fact(DisplayName = "Given an expired key, when ResolveAsync runs, then outcome is Expired")]
    public async Task ExpiredKeyReturnsExpiredAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var store = Substitute.For<IVirtualKeyStore>();
        _ = store.FindAsync("vkey_old", Arg.Any<CancellationToken>()).Returns(new VirtualKey(
            Token: "vkey_old",
            ProjectId: ProjectId.New(),
            Upstream: new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
            ExpiresAt: clock.GetUtcNow().AddSeconds(-1)));
        var resolver = new VirtualKeyResolver(store, clock);

        var resolution = await resolver.ResolveAsync(token: "vkey_old", requestedModel: null, TestContext.Current.CancellationToken);

        resolution.Outcome.ShouldBe(VirtualKeyResolver.ResolveOutcome.Expired);
        resolution.Key.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a request model outside the allow-list, when ResolveAsync runs, then outcome is ModelNotAllowed")]
    public async Task DisallowedModelReturnsModelNotAllowedAsync()
    {
        var store = Substitute.For<IVirtualKeyStore>();
        _ = store.FindAsync("vkey_curated", Arg.Any<CancellationToken>()).Returns(new VirtualKey(
            Token: "vkey_curated",
            ProjectId: ProjectId.New(),
            Upstream: new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
            AllowedModels: ["gpt-4o-mini"]));
        var resolver = new VirtualKeyResolver(store, TimeProvider.System);

        var resolution = await resolver.ResolveAsync(token: "vkey_curated", requestedModel: "gpt-4", TestContext.Current.CancellationToken);

        resolution.Outcome.ShouldBe(VirtualKeyResolver.ResolveOutcome.ModelNotAllowed);
    }

    [Fact(DisplayName = "Given a valid token with no model restriction, when ResolveAsync runs, then outcome is Resolved")]
    public async Task ValidTokenReturnsResolvedAsync()
    {
        var store = Substitute.For<IVirtualKeyStore>();
        _ = store.FindAsync("vkey_open", Arg.Any<CancellationToken>()).Returns(new VirtualKey(
            Token: "vkey_open",
            ProjectId: ProjectId.New(),
            Upstream: new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY")));
        var resolver = new VirtualKeyResolver(store, TimeProvider.System);

        var resolution = await resolver.ResolveAsync(token: "vkey_open", requestedModel: "gpt-4", TestContext.Current.CancellationToken);

        resolution.Outcome.ShouldBe(VirtualKeyResolver.ResolveOutcome.Resolved);
        resolution.Key.ShouldNotBeNull();
    }
}
