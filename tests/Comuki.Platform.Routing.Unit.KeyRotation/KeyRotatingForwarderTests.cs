using Comuki.Platform.Routing.Forwarding;
using Comuki.Platform.Routing.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Routing.Unit.KeyRotation;

public sealed class KeyRotatingForwarderTests
{
    private static KeyRotatingForwarder CreateSut(IKeyPool pool, IUpstreamSender sender)
        => new(pool, sender, NullLogger<KeyRotatingForwarder>.Instance);

    [Fact]
    public async Task ForwardAsync_StreamsSuccess_OnFirstLiveKey()
    {
        var pool = Substitute.For<IKeyPool>();
        pool.Count.Returns(2);
        pool.TryAcquire().Returns("live");
        var sender = Substitute.For<IUpstreamSender>();
        sender.SendOnceAsync(Arg.Any<HttpContext>(), "live", Arg.Any<CancellationToken>())
            .Returns(UpstreamSendResult.Success());
        var context = new DefaultHttpContext();

        await CreateSut(pool, sender).ForwardAsync(context, CancellationToken.None);

        pool.DidNotReceive().MarkExhausted(Arg.Any<string>(), Arg.Any<TimeSpan?>());
        await sender.Received(1).SendOnceAsync(Arg.Any<HttpContext>(), "live", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForwardAsync_RotatesToNextKey_WhenFirstExhausted()
    {
        var pool = Substitute.For<IKeyPool>();
        pool.Count.Returns(2);
        pool.TryAcquire().Returns("dead", "live");
        var sender = Substitute.For<IUpstreamSender>();
        sender.SendOnceAsync(Arg.Any<HttpContext>(), "dead", Arg.Any<CancellationToken>())
            .Returns(UpstreamSendResult.Exhausted(TimeSpan.FromMinutes(5)));
        sender.SendOnceAsync(Arg.Any<HttpContext>(), "live", Arg.Any<CancellationToken>())
            .Returns(UpstreamSendResult.Success());
        var context = new DefaultHttpContext();

        await CreateSut(pool, sender).ForwardAsync(context, CancellationToken.None);

        pool.Received(1).MarkExhausted("dead", TimeSpan.FromMinutes(5));
        await sender.Received(1).SendOnceAsync(Arg.Any<HttpContext>(), "live", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForwardAsync_Returns503_WhenAllKeysExhausted()
    {
        var pool = Substitute.For<IKeyPool>();
        pool.Count.Returns(1);
        pool.TryAcquire().Returns("dead", (string?)null);
        var sender = Substitute.For<IUpstreamSender>();
        sender.SendOnceAsync(Arg.Any<HttpContext>(), "dead", Arg.Any<CancellationToken>())
            .Returns(UpstreamSendResult.Exhausted(null));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await CreateSut(pool, sender).ForwardAsync(context, CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        pool.Received(1).MarkExhausted("dead", null);
    }

    [Fact]
    public async Task ForwardAsync_StopsAndReturns_OnPassThroughError()
    {
        var pool = Substitute.For<IKeyPool>();
        pool.Count.Returns(2);
        pool.TryAcquire().Returns("live");
        var sender = Substitute.For<IUpstreamSender>();
        sender.SendOnceAsync(Arg.Any<HttpContext>(), "live", Arg.Any<CancellationToken>())
            .Returns(UpstreamSendResult.PassedThroughError());
        var context = new DefaultHttpContext();

        await CreateSut(pool, sender).ForwardAsync(context, CancellationToken.None);

        context.Response.StatusCode.ShouldNotBe(StatusCodes.Status503ServiceUnavailable);
        pool.DidNotReceive().MarkExhausted(Arg.Any<string>(), Arg.Any<TimeSpan?>());
        await sender.Received(1).SendOnceAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForwardAsync_Returns503Immediately_WhenPoolEmpty()
    {
        var pool = Substitute.For<IKeyPool>();
        pool.Count.Returns(1);
        pool.TryAcquire().Returns((string?)null);
        var sender = Substitute.For<IUpstreamSender>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await CreateSut(pool, sender).ForwardAsync(context, CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        await sender.DidNotReceive().SendOnceAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
