using Comuki.Host.Translator.Api.Contracts;
using Comuki.Host.Translator.Api.Models.Requests;
using Comuki.Host.Translator.Execution.Loop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Refit;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Lifecycle of <see cref="HeartbeatMonitor"/>: keeps extending the lease
/// until cancelled, returns false on a rejected heartbeat (409 — the
/// reaper took the item) and the same false on a thrown heartbeat (an
/// upstream/network failure that means we cannot prove the lease is
/// still held — the TranslatorLoop's existing lease-lost path takes
/// over without killing the host). Cancellation racing <c>Task.Delay</c>
/// exits cleanly with <c>true</c>.
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
        api.HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => sequence.Dequeue());
        var monitor = new HeartbeatMonitor(api, NullLogger<HeartbeatMonitor>.Instance);

        var held = await monitor.RunAsync(
            workItemId,
            1,
            TimeSpan.FromMilliseconds(1),
            new CancellationTokenSource().Token,
            TestContext.Current.CancellationToken);

        held.ShouldBeFalse();
        await api.Received().HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given the run token trips during the heartbeat delay, when RunAsync runs, then it returns true without further heartbeats")]
    public async Task CancellationDuringDelayReturnsTrueAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        var success = SuccessResponse();
        api.HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(success);
        var monitor = new HeartbeatMonitor(api, NullLogger<HeartbeatMonitor>.Instance);
        using var runSource = new CancellationTokenSource();
        using var stoppingSource = new CancellationTokenSource();
        runSource.CancelAfter(TimeSpan.FromMilliseconds(5));

        var held = await monitor.RunAsync(
            workItemId,
            1,
            TimeSpan.FromMinutes(1),
            runSource.Token,
            stoppingSource.Token);

        held.ShouldBeTrue();
        await api.DidNotReceive().HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an upstream heartbeat call that throws, when RunAsync runs, then it returns false (lease-lost), logs the exception at Error, and does not propagate it")]
    public async Task HeartbeatExceptionIsLoggedAndReturnsFalseAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        api.HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns<IApiResponse>(static _ => throw new HttpRequestException("upstream dropped"));
        var logger = Substitute.For<ILogger<HeartbeatMonitor>>();
        var monitor = new HeartbeatMonitor(api, logger);

        var held = await monitor.RunAsync(
            workItemId,
            1,
            TimeSpan.FromMilliseconds(1),
            new CancellationTokenSource().Token,
            TestContext.Current.CancellationToken);

        // Lease-lost semantics: the loop sees `false` and skips
        // complete/fail without killing the host. The HttpRequestException
        // never reaches the TranslatorLoop's `await heartbeatTask`.
        held.ShouldBeFalse();
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object?>(),
            Arg.Any<HttpRequestException>(),
            Arg.Any<Func<object?, Exception?, string>>());
    }

    [Fact(DisplayName = "Given the run token trips before any heartbeat, when RunAsync runs, then it returns true without ever calling heartbeat")]
    public async Task CancellationBeforeHeartbeatReturnsTrueAsync()
    {
        var api = Substitute.For<IOrchestratorApi>();
        var success = SuccessResponse();
        api.HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(success);
        var monitor = new HeartbeatMonitor(api, NullLogger<HeartbeatMonitor>.Instance);
        using var runSource = new CancellationTokenSource();
        using var stoppingSource = new CancellationTokenSource();
        runSource.Cancel();

        var held = await monitor.RunAsync(
            workItemId,
            1,
            TimeSpan.FromSeconds(10),
            runSource.Token,
            stoppingSource.Token);

        held.ShouldBeTrue();
        await api.DidNotReceive().HeartbeatAsync(workItemId, Arg.Any<HeartbeatWorkItemRequest>(), Arg.Any<CancellationToken>());
    }

    private static IApiResponse SuccessResponse()
    {
        var response = Substitute.For<IApiResponse>();
        response.IsSuccessStatusCode.Returns(true);
        return response;
    }
}
