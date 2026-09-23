using Comuki.Host.Workers.VirtualKeys;
using Comuki.Modules.Proxy.Application.Options;
using Comuki.Modules.Proxy.Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Unit tests for <see cref="MintedVirtualKeyService"/>: the mint gate
/// (proxy enabled + worker base URL), the token/expiry handed to the
/// store, and the terminal-path revocation.
/// </summary>
public sealed class MintedVirtualKeyServiceShould
{
    private readonly IVirtualKeyStore store = Substitute.For<IVirtualKeyStore>();

    private MintedVirtualKeyService NewService(ProxyOptions options)
    {
        return new MintedVirtualKeyService(
            store,
            Options.Create(options),
            NullLogger<MintedVirtualKeyService>.Instance);
    }

    [Fact(DisplayName = "Given an enabled proxy with a worker base URL, when minting, then a base64url token is stored with the lease deadline and returned with the trimmed URL")]
    public async Task MintWhenProxyEnabledAsync()
    {
        var projectId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
        var service = NewService(new ProxyOptions
        {
            Enabled = true,
            WorkerBaseUrl = new Uri("http://comuki-proxy:8080/"),
        });

        var minted = await service.MintAsync(projectId, workItemId, leaseUntil, TestContext.Current.CancellationToken);

        minted.ShouldNotBeNull();
        minted.ProxyBaseUrl.ShouldBe("http://comuki-proxy:8080");
        minted.Token.ShouldNotBeNullOrWhiteSpace();
        // 32 bytes -> 43 base64url chars, no padding
        minted.Token.Length.ShouldBe(43);
        minted.Token.ShouldNotContain('+');
        minted.Token.ShouldNotContain('/');
        await store.Received(1).MintAsync(
            minted.Token,
            Arg.Is<Shared.Kernel.Ids.ProjectId>(id => id.Value == projectId),
            workItemId,
            leaseUntil,
            Arg.Any<IReadOnlyList<string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a disabled proxy, when minting, then nothing is minted and the store is untouched")]
    public async Task NoMintWhenProxyDisabledAsync()
    {
        var service = NewService(new ProxyOptions { Enabled = false, WorkerBaseUrl = new Uri("http://comuki-proxy:8080/") });

        var minted = await service.MintAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), TestContext.Current.CancellationToken);

        minted.ShouldBeNull();
        await store.DidNotReceiveWithAnyArgs().MintAsync(
            default!, default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an enabled proxy without a worker base URL, when minting, then nothing is minted")]
    public async Task NoMintWithoutWorkerBaseUrlAsync()
    {
        var service = NewService(new ProxyOptions { Enabled = true });

        var minted = await service.MintAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), TestContext.Current.CancellationToken);

        minted.ShouldBeNull();
        await store.DidNotReceiveWithAnyArgs().MintAsync(
            default!, default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a minted work item, when revoked, then RemoveAsync runs with the minted token exactly once")]
    public async Task RevokeCallsRemoveWithTheMintedTokenAsync()
    {
        var workItemId = Guid.NewGuid();
        var service = NewService(new ProxyOptions
        {
            Enabled = true,
            WorkerBaseUrl = new Uri("http://comuki-proxy:8080"),
        });
        var minted = await service.MintAsync(Guid.NewGuid(), workItemId, DateTimeOffset.UtcNow.AddMinutes(2), TestContext.Current.CancellationToken);
        minted.ShouldNotBeNull();

        await service.RevokeAsync(workItemId, TestContext.Current.CancellationToken);
        await service.RevokeAsync(workItemId, TestContext.Current.CancellationToken);

        await store.Received(1).RemoveAsync(minted.Token, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unknown work item, when revoked, then the store is untouched")]
    public async Task RevokeUnknownWorkItemIsNoOpAsync()
    {
        var service = NewService(new ProxyOptions { Enabled = true, WorkerBaseUrl = new Uri("http://comuki-proxy:8080") });

        await service.RevokeAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await store.DidNotReceiveWithAnyArgs().RemoveAsync(default!, TestContext.Current.CancellationToken);
    }
}
