using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Contracts.Journal;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Unit tests for <see cref="WorkerStreamJournal"/>: the Start binding, the
/// journaling of activity/report events, and the drop paths (event before
/// Start, unparsable run id).
/// </summary>
public sealed class WorkerStreamJournalShould
{
    private readonly IRunJournal journal = Substitute.For<IRunJournal>();
    private readonly FakeTimeProvider clock = new();

    private WorkerStreamJournal CreateJournal()
    {
        return new WorkerStreamJournal(journal, clock, NullLogger<WorkerStreamJournal>.Instance);
    }

    private static WorkerEvent StartEvent(Guid runId)
    {
        return new WorkerEvent
        {
            Start = new StageStart { WorkItemId = Guid.NewGuid().ToString(), RunId = runId.ToString(), Brief = "do it" },
        };
    }

    [Fact(DisplayName = "Given a Start event, when appended, then the stream binds to the run and journals it")]
    public async Task BindOnStartAndJournalItAsync()
    {
        var runId = Guid.NewGuid();
        var streamJournal = CreateJournal();

        await streamJournal.AppendAsync(StartEvent(runId), TestContext.Current.CancellationToken);

        streamJournal.RunId.ShouldNotBeNull();
        streamJournal.RunId.Value.Value.ShouldBe(runId);
        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(entry =>
                entry.RunId.Value == runId
                && entry.Type == RunEventTypes.WorkerReported
                && entry.PayloadJson.Contains("do it", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a bound stream, when an activity arrives, then it is journaled on the same run")]
    public async Task JournalActivityAfterBindingAsync()
    {
        var runId = Guid.NewGuid();
        var streamJournal = CreateJournal();
        await streamJournal.AppendAsync(StartEvent(runId), TestContext.Current.CancellationToken);

        await streamJournal.AppendAsync(
            new WorkerEvent { Activity = new StageActivity { WorkItemId = "1", Text = "thinking..." } },
            TestContext.Current.CancellationToken);

        await journal.Received(2).AppendAsync(Arg.Any<RunEventEntry>(), Arg.Any<CancellationToken>());
        await journal.Received(1).AppendAsync(
            Arg.Is<RunEventEntry>(static entry => entry.PayloadJson.Contains("thinking...", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no Start yet, when an activity arrives, then it is dropped, not journaled")]
    public async Task DropActivityBeforeStartAsync()
    {
        var streamJournal = CreateJournal();

        await streamJournal.AppendAsync(
            new WorkerEvent { Report = new StageReport { WorkItemId = "1", Status = "success" } },
            TestContext.Current.CancellationToken);

        streamJournal.RunId.ShouldBeNull();
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a Start with an unparsable run id, when events arrive, then nothing is journaled")]
    public async Task DropStreamWithUnparsableRunIdAsync()
    {
        var streamJournal = CreateJournal();
        await streamJournal.AppendAsync(
            new WorkerEvent { Start = new StageStart { RunId = "not-a-guid" } },
            TestContext.Current.CancellationToken);

        await streamJournal.AppendAsync(
            new WorkerEvent { Activity = new StageActivity { Text = "orphan" } },
            TestContext.Current.CancellationToken);

        streamJournal.RunId.ShouldBeNull();
        await journal.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an event with no stage payload, when appended, then no journal write happens")]
    public async Task SkipEmptyWorkerEventAsync()
    {
        var runId = Guid.NewGuid();
        var streamJournal = CreateJournal();
        await streamJournal.AppendAsync(StartEvent(runId), TestContext.Current.CancellationToken);

        await streamJournal.AppendAsync(new WorkerEvent(), TestContext.Current.CancellationToken);

        await journal.Received(1).AppendAsync(Arg.Any<RunEventEntry>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>Deterministic clock for journal timestamps.</summary>
internal sealed class FakeTimeProvider : TimeProvider;
