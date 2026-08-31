using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Unit tests for the <see cref="WorkerCommandHub"/>: channel registry
/// lifecycle and the best-effort send surface of <see cref="IWorkerCommandPipe"/>.
/// </summary>
public sealed class WorkerCommandHubShould
{
    [Fact(DisplayName = "Given a registered worker, when TrySendStop, then the command lands in its channel")]
    public async Task DeliverStopToRegisteredWorkerAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var channel = hub.Register(workerId);

        var delivered = hub.TrySendStop(workerId, "user cancelled");

        delivered.ShouldBeTrue();
        var command = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        command.Stop.ShouldNotBeNull();
        command.Stop.Reason.ShouldBe("user cancelled");
    }

    [Fact(DisplayName = "Given a registered worker, when inject and lease-expire, then each command carries its payload")]
    public async Task DeliverInjectContextAndLeaseExpiredAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var channel = hub.Register(workerId);

        hub.TrySendInjectContext(workerId, "PR comment: use the other lib").ShouldBeTrue();
        hub.TrySendLeaseExpired(workerId).ShouldBeTrue();

        var first = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        first.InjectContext.ShouldNotBeNull();
        first.InjectContext.Context.ShouldBe("PR comment: use the other lib");
        var second = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        second.LeaseExpired.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given no live stream, when TrySendStop, then it is a miss, not an error")]
    public void MissWorkerWithoutStreamAsync()
    {
        var hub = new WorkerCommandHub();

        hub.TrySendStop(WorkerId.New(), "reason").ShouldBeFalse();
        hub.TrySendInjectContext(WorkerId.New(), "ctx").ShouldBeFalse();
        hub.TrySendLeaseExpired(WorkerId.New()).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an unregistered worker, when TrySendStop, then it is a miss")]
    public void MissWorkerAfterUnregisterAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var channel = hub.Register(workerId);
        hub.Unregister(workerId, channel);

        hub.TrySendStop(workerId, "late").ShouldBeFalse();
        channel.Reader.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a reconnecting worker, when it registers again, then the old channel is replaced")]
    public void ReplaceChannelOnReconnectAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var first = hub.Register(workerId);
        var second = hub.Register(workerId);

        hub.TrySendStop(workerId, "stop");
        first.Reader.Completion.IsCompleted.ShouldBeTrue();

        second.Reader.TryRead(out var command).ShouldBeTrue();
        command.Stop.ShouldNotBeNull();
    }
}
