using Comuki.Modules.Proxy.Application.Budgeting;
using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>
/// Per-request guards (Q18 budget → 402; Q19 token caps → 400) over
/// <see cref="ProxyRequestGuards"/>. The enforcer is mocked — the guard
/// composes the verdict, the enforcer owns the verdict shape.
/// </summary>
public sealed class ProxyRequestGuardsShould
{
    private const int StatusPaymentRequired = 402;
    private const int StatusBadRequest = 400;

    private readonly IProxyBudgetEnforcer enforcer = Substitute.For<IProxyBudgetEnforcer>();

    [Fact(DisplayName = "Given a key with no caps, when the guard runs, then the request is allowed")]
    public async Task AllowUncappedKeyAsync()
    {
        var key = BuildKey(BudgetUsd: null, MaxInputTokens: null, MaxOutputTokens: null);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: null, SpentUsdMicros: 0, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 0, requestedMaxOutputTokens: null, TestContext.Current.CancellationToken);

        rejection.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a key whose monthly budget is exhausted, when the guard runs, then a 402 PaymentRequired rejection is returned")]
    public async Task DenyOverBudgetKeyAsync()
    {
        var key = BuildKey(BudgetUsd: 10m);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: false, CapUsdMicros: 10_000_000, SpentUsdMicros: 10_000_000, RetryAfterSeconds: 1024));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 0, requestedMaxOutputTokens: null, TestContext.Current.CancellationToken);

        rejection.ShouldNotBeNull();
        rejection.StatusCode.ShouldBe(StatusPaymentRequired);
        rejection.Code.ShouldBe(ProxyRequestGuards.BudgetExceededCode);
        rejection.Detail.ShouldContain("monthly budget exceeded");
        await enforcer.Received(1).EvaluateAsync(key, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a key under budget, when the guard runs, then the request is allowed even if the request body is small")]
    public async Task AllowUnderBudgetSmallBodyAsync()
    {
        var key = BuildKey(BudgetUsd: 10m, MaxInputTokens: 1000);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: 10_000_000, SpentUsdMicros: 1_000_000, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 200, requestedMaxOutputTokens: 256, TestContext.Current.CancellationToken);

        rejection.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a key with MaxInputTokens and a body whose Content-Length / 4 exceeds it, when the guard runs, then a 400 input-tokens rejection is returned")]
    public async Task DenyOverInputCapAsync()
    {
        var key = BuildKey(MaxInputTokens: 100);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: null, SpentUsdMicros: 0, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 800, requestedMaxOutputTokens: 64, TestContext.Current.CancellationToken);

        rejection.ShouldNotBeNull();
        rejection.StatusCode.ShouldBe(StatusBadRequest);
        rejection.Code.ShouldBe(ProxyRequestGuards.InputTokensExceededCode);
        rejection.Detail.ShouldContain("MaxInputTokens cap of 100");
    }

    [Fact(DisplayName = "Given a key with MaxOutputTokens and a request asking for more, when the guard runs, then a 400 output-tokens rejection is returned")]
    public async Task DenyOverOutputCapAsync()
    {
        var key = BuildKey(MaxOutputTokens: 256);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: null, SpentUsdMicros: 0, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 200, requestedMaxOutputTokens: 1024, TestContext.Current.CancellationToken);

        rejection.ShouldNotBeNull();
        rejection.StatusCode.ShouldBe(StatusBadRequest);
        rejection.Code.ShouldBe(ProxyRequestGuards.OutputTokensExceededCode);
        rejection.Detail.ShouldContain("MaxOutputTokens cap of 256");
    }

    [Fact(DisplayName = "Given a key with MaxOutputTokens but a request that omits the field, when the guard runs, then no output rejection is emitted")]
    public async Task SkipOutputCapWhenAbsentAsync()
    {
        var key = BuildKey(MaxOutputTokens: 256);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: null, SpentUsdMicros: 0, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 200, requestedMaxOutputTokens: null, TestContext.Current.CancellationToken);

        rejection.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a request that fits every guard, when the guard runs, then the order is budget first and the request is allowed")]
    public async Task OrderIsBudgetFirstAsync()
    {
        var key = BuildKey(BudgetUsd: 10m, MaxInputTokens: 100, MaxOutputTokens: 64);
        enforcer.EvaluateAsync(key, Arg.Any<CancellationToken>())
            .Returns(new ProxyBudgetVerdict(Allowed: true, CapUsdMicros: 10_000_000, SpentUsdMicros: 1_000_000, RetryAfterSeconds: 0));

        var rejection = await ProxyRequestGuards.EvaluateAsync(
            key, enforcer, contentLength: 80, requestedMaxOutputTokens: 32, TestContext.Current.CancellationToken);

        rejection.ShouldBeNull();
    }

    private static VirtualKey BuildKey(
        decimal? BudgetUsd = null,
        int? MaxInputTokens = null,
        int? MaxOutputTokens = null)
    {
        return new VirtualKey(
            Token: "vkey_test",
            ProjectId: ProjectId.New(),
            Upstream: new UpstreamSpec("openai", "https://api.openai.com", "OPENAI_API_KEY"),
            BudgetUsd: BudgetUsd,
            ExpiresAt: null,
            AllowedModels: null,
            MaxInputTokens: MaxInputTokens,
            MaxOutputTokens: MaxOutputTokens);
    }
}
