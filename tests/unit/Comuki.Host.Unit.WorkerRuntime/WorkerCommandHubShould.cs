using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Contracts.Grpc;
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
        hub.TrySendTurnInput(WorkerId.New(), new TurnInput { Text = "any" }).ShouldBeFalse();
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

    [Fact(DisplayName = "Given a registered worker, when two TrySendTurnInput land, then both are delivered in order (Phase 1c verify: \"two commands on a connected stream are delivered in order\")")]
    public async Task DeliverTwoTurnInputsInOrderAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var channel = hub.Register(workerId);

        hub.TrySendTurnInput(workerId, new TurnInput { Text = "first", Role = "user" }).ShouldBeTrue();
        hub.TrySendTurnInput(workerId, new TurnInput { Text = "second", Role = "user" }).ShouldBeTrue();

        var first = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        first.TurnInput.ShouldNotBeNull();
        first.TurnInput.Text.ShouldBe("first");
        var second = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        second.TurnInput.ShouldNotBeNull();
        second.TurnInput.Text.ShouldBe("second");
    }

    [Fact(DisplayName = "Given a worker with no live stream, when TrySendTurnInput, then it is a miss (does not throw) (Phase 1c verify: \"a command without a stream returns false and does not throw\")")]
    public void TurnInputMissesWithoutStreamAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();

        hub.TrySendTurnInput(workerId, new TurnInput { Text = "any" }).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a registered worker, when TrySendTurnInput carries Text/Role/Metadata, then the channel reads the same fields end-to-end")]
    public async Task TurnInputCarriesTextRoleMetadataAsync()
    {
        var hub = new WorkerCommandHub();
        var workerId = WorkerId.New();
        var channel = hub.Register(workerId);
        var metadata = new Dictionary<string, string>
        {
            ["as"] = "steer",
            ["origin"] = "operator-steer",
        };

        hub.TrySendTurnInput(workerId, new TurnInput
        {
            Text = "the operator sees this on the bidi stream",
            Role = "user",
            Metadata = metadata,
        }).ShouldBeTrue();

        var command = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        command.TurnInput.ShouldNotBeNull();
        command.TurnInput.Text.ShouldBe("the operator sees this on the bidi stream");
        command.TurnInput.Role.ShouldBe("user");
        command.TurnInput.Metadata.ShouldContainKey("as");
        command.TurnInput.Metadata["as"].ShouldBe("steer");
        command.TurnInput.Metadata["origin"].ShouldBe("operator-steer");
    }
}
