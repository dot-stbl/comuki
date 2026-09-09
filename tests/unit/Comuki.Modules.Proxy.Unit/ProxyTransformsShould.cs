using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Modules.Proxy.Infrastructure.Auth;
using Comuki.Modules.Proxy.Infrastructure.Yarp;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using Yarp.ReverseProxy.Transforms;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// <see cref="ProxyTransforms"/>: the request transform resolves the
/// virtual-key claim into the upstream API key (reading the env-var the
/// upstream spec points at), and runs the per-request guards before
/// swapping the <c>Authorization</c> header. The response transform is a
/// reserved no-op in v1 (body-based metering deferred). Tests cover the
/// happy path (env-var resolved, header rewritten), the short-circuits
/// for each guard, and the JSON body peek for <c>max_tokens</c>.
/// </summary>
public sealed class ProxyTransformsShould
{
    private const string TestEnvVar = "PROXY_TRANSFORMS_TEST_API_KEY";

    [Fact(DisplayName = "Given a request whose virtual-key claim resolves and the upstream env-var is set, when the request transform runs, then the outbound Authorization header is replaced with the upstream key")]
    public async Task RequestTransformRewritesAuthorizationHeaderAsync()
    {
        var upstreamKey = "upstream-sk-1234567890abcdef";
        Environment.SetEnvironmentVariable(TestEnvVar, upstreamKey);
        try
        {
            var key = NewVirtualKey(apiKeyEnvRef: TestEnvVar);
            var context = NewRequestContext(
                    virtualKeyToken: key.Token,
                    requestServices: NewServices(key: key, verdictAllowed: true));
            var transformer = ProxyTransforms.RewriteAuthFromVirtualKeyAsync;

            await transformer(context);

            var auth = context.ProxyRequest.Headers.Authorization;
            auth.ShouldNotBeNull();
            auth.Scheme.ShouldBe("Bearer");
            auth.Parameter.ShouldBe(upstreamKey);
            context.ProxyRequest.Headers.Host.ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TestEnvVar, null);
        }
    }

    [Fact(DisplayName = "Given a request whose monthly budget is exhausted, when the request transform runs, then it short-circuits with a 402 problem+json body and never overwrites the Authorization header")]
    public async Task BudgetGuardShortCircuitsWith402Async()
    {
        var key = NewVirtualKey(apiKeyEnvRef: TestEnvVar);
        var context = NewRequestContext(
            virtualKeyToken: key.Token,
            requestServices: NewServices(
                key: key,
                verdictAllowed: false,
                verdict: new ProxyBudgetVerdict(Allowed: false, CapUsdMicros: 10_000_000, SpentUsdMicros: 10_000_000, RetryAfterSeconds: 1024)),
            preExistingAuth: new AuthenticationHeaderValue("Bearer", "stale-original"));
        var transformer = ProxyTransforms.RewriteAuthFromVirtualKeyAsync;

        await transformer(context);

        context.HttpContext.Response.StatusCode.ShouldBe(402);
        context.HttpContext.Response.ContentType.ShouldBe("application/problem+json");
        context.ProxyRequest.Headers.Authorization!.Parameter.ShouldBe("stale-original");
    }

    [Fact(DisplayName = "Given a request whose body declares more than MaxOutputTokens, when the request transform runs, then it short-circuits with 400 (Q19 output-token cap rejection)")]
    public async Task OutputTokenCapShortCircuitsWith400Async()
    {
        var key = NewVirtualKey(apiKeyEnvRef: TestEnvVar, maxOutputTokens: 64);
        var context = NewRequestContext(
            virtualKeyToken: key.Token,
            requestServices: NewServices(key: key, verdictAllowed: true),
            body: /*lang=json,strict*/ """{"max_tokens":512}""");
        var transformer = ProxyTransforms.RewriteAuthFromVirtualKeyAsync;

        await transformer(context);

        context.HttpContext.Response.StatusCode.ShouldBe(400);
        context.HttpContext.Response.ContentType.ShouldBe("application/problem+json");
    }

    [Fact(DisplayName = "Given a body that does not declare max_tokens at all, when the peek parses the JSON, then the result is null and the request is allowed")]
    public async Task BodyWithoutMaxTokensReturnsNullAsync()
    {
        var request = NewHttpRequest(body: /*lang=json,strict*/ """{"model":"gpt-4"}""");

        var observed = await ProxyTransforms.TryReadMaxOutputTokensAsync(request, TestContext.Current.CancellationToken);

        observed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a body declaring max_tokens, when the peek parses the JSON, then the integer value is returned")]
    public async Task BodyWithMaxTokensReturnsDeclaredValueAsync()
    {
        var request = NewHttpRequest(body: /*lang=json,strict*/ """{"max_tokens":256}""");

        var observed = await ProxyTransforms.TryReadMaxOutputTokensAsync(request, TestContext.Current.CancellationToken);

        observed.ShouldBe(256);
    }

    [Fact(DisplayName = "Given the response transform, when it runs, then it completes without throwing (the v1 no-op contract — body-based metering is deferred)")]
    public async Task ResponseTransformIsANoOpAsync()
    {
        var httpContext = new DefaultHttpContext();
        var proxyResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var context = new ResponseTransformContext
        {
            HttpContext = httpContext,
            ProxyResponse = proxyResponse,
        };

        await ProxyTransforms.MeterUsageFromResponseAsync(context);

        httpContext.Response.HasStarted.ShouldBeFalse();
    }

    private static VirtualKey NewVirtualKey(string apiKeyEnvRef, int? maxOutputTokens = null)
    {
        return new VirtualKey(
            Token: "vkey_transforms_test",
            ProjectId: ProjectId.New(),
            Upstream: new UpstreamSpec("openai", "https://api.openai.com", apiKeyEnvRef),
            BudgetUsd: null,
            MaxOutputTokens: maxOutputTokens);
    }

    private static IServiceProvider NewServices(VirtualKey key, bool verdictAllowed, ProxyBudgetVerdict? verdict = null)
    {
        var store = Substitute.For<IVirtualKeyStore>();
        store.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(key);

        var enforcer = Substitute.For<IProxyBudgetEnforcer>();
        enforcer.EvaluateAsync(Arg.Any<VirtualKey>(), Arg.Any<CancellationToken>())
            .Returns(verdict ?? new ProxyBudgetVerdict(verdictAllowed, null, 0, 0));

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(enforcer);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services.BuildServiceProvider();
    }

    private static RequestTransformContext NewRequestContext(
        string virtualKeyToken,
        IServiceProvider requestServices,
        string body = /*lang=json,strict*/ """{"model":"gpt-4"}""",
        AuthenticationHeaderValue? preExistingAuth = null)
    {
        var proxyRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("https://proxy.local/api/v1/chat"),
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        proxyRequest.Headers.Authorization = preExistingAuth;

        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices,
        };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/v1/chat";
        httpContext.Request.ContentLength = body.Length;
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body))
        {
            Position = 0
        };
        var identity = new ClaimsIdentity(
            [new Claim(ProxyClaimNames.VirtualKey, virtualKeyToken)],
            authenticationType: "VirtualKey");
        httpContext.User = new ClaimsPrincipal(identity);

        return new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = proxyRequest,
            CancellationToken = TestContext.Current.CancellationToken,
        };
    }

    private static HttpRequest NewHttpRequest(string body)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body))
        {
            Position = 0
        };
        httpContext.Request.ContentLength = body.Length;
        return httpContext.Request;
    }
}
