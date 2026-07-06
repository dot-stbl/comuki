using Comuki.Platform.Routing.Forwarding;
using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Comuki.Platform.Routing.Unit.KeyRotation;

public sealed class KeyRotatingForwarderShould
{
    private static readonly IOptions<RotationOptions> defaultOptions = MsOptions.Create(new RotationOptions
    {
        ApiKeys = ["k"],
        UpstreamUrl = "https://x.example",
        ExhaustionRules = [new ExhaustionRule { StatusCode = 429 }],
    });

    private static KeyRotatingForwarder CreateSut(IKeyPool pool, IUpstreamSender sender)
        => new(pool, sender, defaultOptions, NullLogger<KeyRotatingForwarder>.Instance);

    [Fact(DisplayName = "Given a live key, when forwarding, then streams success on the first attempt without rotating")]
    public async Task StreamSuccessWithoutRotation()
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

    [Fact(DisplayName = "Given the first key is exhausted, when forwarding, then marks it exhausted and rotates to a live key")]
    public async Task RotateToNextKeyWhenFirstExhausted()
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

    [Fact(DisplayName = "Given every key is exhausted, when forwarding, then responds 503")]
    public async Task Return503WhenAllKeysExhausted()
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

    [Fact(DisplayName = "Given a non-quota upstream error, when forwarding, then stops without rotating the key")]
    public async Task StopWithoutRotationOnPassThroughError()
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

    [Fact(DisplayName = "Given the pool is empty, when forwarding, then responds 503 without calling the upstream")]
    public async Task Return503ImmediatelyWhenPoolEmpty()
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
