using Comuki.Host.Correlation;
using Comuki.Shared.Bootstrap.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli.Correlation;

/// <summary>
/// Request correlation middleware (issue #56 §5): a valid incoming
/// X-Request-Id is reused, a missing or hostile one is replaced, the id
/// lands in the ambient accessor for the request pipeline and on the
/// response header, and the scope is gone after the pipeline completes.
/// </summary>
public sealed class CorrelationIdMiddlewareShould
{
    [Fact(DisplayName = "Given a valid incoming X-Request-Id, when the middleware runs, then it is reused inside the pipeline and echoed on the response")]
    public async Task ReuseValidIncomingIdAsync()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();
        string? observedInPipeline = null;
        var context = Context(accessor);
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "operator-req-42";

        await InvokeAsync(context, async _ =>
        {
            observedInPipeline = accessor.CurrentId;
            await Task.Yield();
        });

        observedInPipeline.ShouldBe("operator-req-42");
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().ShouldBe("operator-req-42");
        accessor.CurrentId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given no incoming header, when the middleware runs, then a fresh 32-hex id is installed and echoed")]
    public async Task GenerateIdWhenMissingAsync()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();
        var context = Context(accessor);

        await InvokeAsync(context, static _ => Task.CompletedTask);

        var responseId = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        responseId.ShouldMatch("^[0-9a-f]{32}$");
        accessor.CurrentId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a hostile incoming header, when the middleware runs, then it is replaced, not echoed")]
    public async Task ReplaceHostileIncomingIdAsync()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();
        var context = Context(accessor);
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "bad id with spaces";

        await InvokeAsync(context, static _ => Task.CompletedTask);

        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().ShouldMatch("^[0-9a-f]{32}$");
    }

    [Theory(DisplayName = "Given a too-short or too-long incoming id, when the middleware runs, then it is replaced")]
    [InlineData("abc")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task ReplaceOutOfWindowIdAsync(string incoming)
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();
        var context = Context(accessor);
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;

        await InvokeAsync(context, static _ => Task.CompletedTask);

        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().ShouldMatch("^[0-9a-f]{32}$");
    }

    private static Task InvokeAsync(HttpContext context, Func<HttpContext, Task> pipeline)
    {
        return new CorrelationIdMiddleware(inner => pipeline(inner)).InvokeAsync(context);
    }

    private static DefaultHttpContext Context(AsyncLocalCorrelationIdAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICorrelationIdAccessor>(accessor);
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
    }
}
