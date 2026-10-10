using Comuki.Host.Translator.Api.Contracts;
using Comuki.Host.Translator.Execution.Loop;
using NSubstitute;
using Refit;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Lifecycle of <see cref="HeartbeatMonitor"/>: keeps extending the lease
/// until cancelled, returns false on a rejected heartbeat (409 — the
/// reaper took the item), surfaces an upstream failure as a propagated
/// exception. Cancellation racing <c>Task.Delay</c> exits cleanly with
/// <c>true</c>.
/// </summary>
public sealed class HeartbeatMonitorShould
{
    private static readonly Guid workItemId = Guid.NewGuid();

    [Fact(DisplayName = "Given a series of successful heartbeats followed by a rejected one, when RunAsync runs, then it returns false on the rejection")]
    public async Task RejectedHeartbeatReturnsFalseAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        var success = Substitute.For<IApiResponse>();
        success.IsSuccessStatusCode.Returns(true);
        var rejection = Substitute.For<IApiResponse>();
        rejection.IsSuccessStatusCode.Returns(false);
        var sequence = new Queue<IApiResponse>();
        sequence.Enqueue(success);
        sequence.Enqueue(success);
        sequence.Enqueue(rejection);
        api.HeartbeatAsync(workItemId, Arg.Any<CancellationToken>())
            .Returns(_ => sequence.Dequeue());
        var monitor = new HeartbeatMonitor(api);

        var held = await monitor.RunAsync(
            workItemId,
            TimeSpan.FromMilliseconds(1),
            new CancellationTokenSource().Token,
            TestContext.Current.CancellationToken);

        held.ShouldBeFalse();
        await api.Received().HeartbeatAsync(workItemId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the run token trips during the heartbeat delay, when RunAsync runs, then it returns true without further heartbeats")]
    public async Task CancellationDuringDelayReturnsTrueAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        var success = SuccessResponse();
        api.HeartbeatAsync(workItemId, Arg.Any<CancellationToken>())
            .Returns(success);
        var monitor = new HeartbeatMonitor(api);
        using var runSource = new CancellationTokenSource();
        using var stoppingSource = new CancellationTokenSource();
        runSource.CancelAfter(TimeSpan.FromMilliseconds(5));

        var held = await monitor.RunAsync(
            workItemId,
            TimeSpan.FromMinutes(1),
            runSource.Token,
            stoppingSource.Token);

        held.ShouldBeTrue();
        await api.DidNotReceive().HeartbeatAsync(workItemId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an upstream heartbeat call that throws, when RunAsync runs, then the exception propagates")]
    public async Task HeartbeatExceptionPropagatesAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        api.HeartbeatAsync(workItemId, Arg.Any<CancellationToken>())
            .Returns<IApiResponse>(_ => throw new HttpRequestException("upstream dropped"));
        var monitor = new HeartbeatMonitor(api);

        await Should.ThrowAsync<HttpRequestException>(
            async () => await monitor.RunAsync(
                workItemId,
                TimeSpan.FromMilliseconds(1),
                new CancellationTokenSource().Token,
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given the run token trips before any heartbeat, when RunAsync runs, then it returns true without ever calling heartbeat")]
    public async Task CancellationBeforeHeartbeatReturnsTrueAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        var success = SuccessResponse();
        api.HeartbeatAsync(workItemId, Arg.Any<CancellationToken>())
            .Returns(success);
        var monitor = new HeartbeatMonitor(api);
        using var runSource = new CancellationTokenSource();
        runSource.Cancel();

        var held = await monitor.RunAsync(
            workItemId,
            TimeSpan.FromSeconds(10),
            runSource.Token,
            TestContext.Current.CancellationToken);

        held.ShouldBeTrue();
        await api.DidNotReceive().HeartbeatAsync(workItemId, Arg.Any<CancellationToken>());
    }

    private static IApiResponse SuccessResponse()
    {
        var response = Substitute.For<IApiResponse>();
        response.IsSuccessStatusCode.Returns(true);
        return response;
    }
}
