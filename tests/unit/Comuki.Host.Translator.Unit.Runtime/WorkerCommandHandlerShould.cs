using Comuki.Host.Translator.Execution.Commands;
using Comuki.Shared.Contracts.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Per-command dispatch of <see cref="WorkerCommandHandler"/>: Stop cancels
/// the run and flags <c>StopRequested</c>, <see cref="InjectContext"/>
/// appends to the working-directory file, <see cref="LeaseExpired"/>
/// flags <c>LeaseLost</c>. The loop exits when the orchestrator closes
/// the command stream.
/// </summary>
public sealed class WorkerCommandHandlerShould
{
    [Fact(DisplayName = "Given a Stop command, when ConsumeAsync runs, then the run cancellation is tripped and StopRequested is set")]
    public async Task StopCommandCancelsAndFlagsStopRequestedAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { Stop = new Stop { Reason = "operator-pressed-cancel" } },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        runSource.IsCancellationRequested.ShouldBeTrue();
        run.StopRequested.ShouldBeTrue();
        run.LeaseLost.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given a LeaseExpired command, when ConsumeAsync runs, then the run cancellation is tripped and LeaseLost is set")]
    public async Task LeaseExpiredFlagsLeaseLostAndCancelsAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { LeaseExpired = new LeaseExpired() },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        runSource.IsCancellationRequested.ShouldBeTrue();
        run.LeaseLost.ShouldBeTrue();
        run.StopRequested.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given an InjectContext command, when ConsumeAsync runs, then the context is appended to the working-directory file")]
    public async Task InjectContextAppendsToWorkingFileAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"wch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var service = Substitute.For<IWorkerService>();
            WorkerSessionTestHelpers.StubCommandStream(service,
            [
                new OrchestratorCommand { InjectContext = new InjectContext { Context = "new-context-block" } },
            ]);
            var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
            var handler = new WorkerCommandHandler(run, tempDirectory, NullLogger<WorkerCommandHandler>.Instance);

            await handler.ConsumeAsync(TestContext.Current.CancellationToken);

            var filePath = Path.Combine(tempDirectory, "comuki-injected-context.md");
            File.Exists(filePath).ShouldBeTrue();
            (await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken))
                .ShouldContain("new-context-block");
            run.StopRequested.ShouldBeFalse();
            run.LeaseLost.ShouldBeFalse();
            await run.Session.CloseAsync();
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact(DisplayName = "Given a sequence of Stop and LeaseExpired, when ConsumeAsync runs, then both flags are set")]
    public async Task SequenceOfCommandsAccumulatesStateAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { Stop = new Stop { Reason = "first" } },
            new OrchestratorCommand { LeaseExpired = new LeaseExpired() },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        run.StopRequested.ShouldBeTrue();
        run.LeaseLost.ShouldBeTrue();
        runSource.IsCancellationRequested.ShouldBeTrue();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given no commands, when ConsumeAsync runs, then it returns when the stream ends without changing state")]
    public async Task EmptyStreamReturnsWithoutStateChangeAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        WorkerSessionTestHelpers.StubCommandStream(service, []);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        run.StopRequested.ShouldBeFalse();
        run.LeaseLost.ShouldBeFalse();
        runSource.IsCancellationRequested.ShouldBeFalse();
        await run.Session.CloseAsync();
    }
}
