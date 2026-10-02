using Comuki.Host.Translator.Execution.Commands;
using Comuki.Shared.Contracts.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Per-command dispatch of <see cref="WorkerCommandHandler"/>: Stop cancels
/// the run and flags <c>StopRequested</c>, <see cref="InjectContext"/>
/// appends to the working-directory file, <see cref="LeaseExpired"/>
/// flags <c>LeaseLost</c>, <see cref="Exec"/> is the debug-only operator
/// surface (refused when <c>DebugExec</c> is off, dispatched to
/// <see cref="IDebugExecHost"/> when on). The loop exits when the
/// orchestrator closes the command stream.
/// </summary>
public sealed class WorkerCommandHandlerShould
{
    [Fact(DisplayName = "Given a Stop command, when ConsumeAsync runs, then the run cancellation is tripped and StopRequested is set")]
    public async Task StopCommandCancelsAndFlagsStopRequestedAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { Stop = new Stop { Reason = "operator-pressed-cancel" } },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: false));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

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
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { LeaseExpired = new LeaseExpired() },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: false));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

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
            var debugExecHost = Substitute.For<IDebugExecHost>();
            WorkerSessionTestHelpers.StubCommandStream(service,
            [
                new OrchestratorCommand { InjectContext = new InjectContext { Context = "new-context-block" } },
            ]);
            var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
            var options = Options.Create(NewOptions(debugExec: false));
            var handler = new WorkerCommandHandler(run, tempDirectory, options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

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
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand { Stop = new Stop { Reason = "first" } },
            new OrchestratorCommand { LeaseExpired = new LeaseExpired() },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: false));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

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
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service, []);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: false));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        run.StopRequested.ShouldBeFalse();
        run.LeaseLost.ShouldBeFalse();
        runSource.IsCancellationRequested.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given an Exec command and DebugExec=false (default), when ConsumeAsync runs, then the exec is refused and the host is not invoked")]
    public async Task ExecRefusedWhenFlagIsOffAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand
            {
                Exec = new Exec
                {
                    Command = "/bin/sh",
                    Arguments = ["-c", "echo pwn"],
                },
            },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: false));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        // Spec scenario "Default refuses exec": the call is refused when
        // the flag is off. The handler MUST NOT reach the host, MUST NOT
        // cancel the run, MUST NOT crash the stream loop.
        await debugExecHost.DidNotReceive().RunAsync(Arg.Any<DebugExecRequest>(), Arg.Any<CancellationToken>());
        runSource.IsCancellationRequested.ShouldBeFalse();
        run.StopRequested.ShouldBeFalse();
        run.LeaseLost.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given an Exec command and DebugExec=true, when ConsumeAsync runs, then the host is invoked with the requested command and arguments")]
    public async Task ExecDispatchedWhenFlagIsOnAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        var debugExecHost = Substitute.For<IDebugExecHost>();
        debugExecHost.RunAsync(Arg.Any<DebugExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DebugExecOutcome(ExitCode: 0, FailureDetail: null));
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"wch-exec-{Guid.NewGuid():N}");
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand
            {
                Exec = new Exec
                {
                    Command = "/bin/sh",
                    Arguments = ["-c", "echo pwn"],
                },
            },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: true));
        var handler = new WorkerCommandHandler(run, workingDirectory, options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        // Spec scenario "Debug on": an operator with the matching
        // permission may exec into the Translator container. The
        // handler dispatches to the host and the host is called with
        // the verbatim command, arguments and working directory.
        await debugExecHost.Received(1).RunAsync(
            Arg.Is<DebugExecRequest>(request =>
                request.Command == "/bin/sh"
                && request.Arguments.SequenceEqual(new[] { "-c", "echo pwn" })
                && request.WorkingDirectory == workingDirectory),
            Arg.Any<CancellationToken>());
        run.StopRequested.ShouldBeFalse();
        run.LeaseLost.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    [Fact(DisplayName = "Given an Exec command with an empty Command and DebugExec=true, when ConsumeAsync runs, then the exec is refused as malformed")]
    public async Task ExecRefusedWhenCommandIsEmptyAsync()
    {
        var workItemId = Guid.NewGuid();
        using var runSource = new CancellationTokenSource();
        var service = Substitute.For<IWorkerService>();
        var debugExecHost = Substitute.For<IDebugExecHost>();
        WorkerSessionTestHelpers.StubCommandStream(service,
        [
            new OrchestratorCommand
            {
                Exec = new Exec
                {
                    Command = string.Empty,
                    Arguments = [],
                },
            },
        ]);
        var run = WorkerSessionTestHelpers.NewRun(service, workItemId, runSource);
        var options = Options.Create(NewOptions(debugExec: true));
        var handler = new WorkerCommandHandler(run, Path.GetTempPath(), options, debugExecHost, NullLogger<WorkerCommandHandler>.Instance);

        await handler.ConsumeAsync(TestContext.Current.CancellationToken);

        // The flag is on but the command is empty — treated as
        // misroute. Same refusal shape as flag-off: no host call, no
        // run cancellation, stream keeps consuming.
        await debugExecHost.DidNotReceive().RunAsync(Arg.Any<DebugExecRequest>(), Arg.Any<CancellationToken>());
        runSource.IsCancellationRequested.ShouldBeFalse();
        run.StopRequested.ShouldBeFalse();
        run.LeaseLost.ShouldBeFalse();
        await run.Session.CloseAsync();
    }

    /// <summary>
    /// Builds a <see cref="TranslatorOptions"/>
    /// with the required fields and the test-supplied <c>DebugExec</c>
    /// value. The translator options class lives in the production
    /// assembly; this factory keeps the test hermetic.
    /// </summary>
    private static TranslatorOptions NewOptions(bool debugExec)
    {
        return new TranslatorOptions
        {
            OrchestratorBaseUrl = new Uri("http://orchestrator.test/"),
            OrchestratorGrpcUrl = new Uri("http://orchestrator.test/"),
            WorkerToken = new string('t', 32),
            ProfileKey = "test-profile",
            ProfilesRef = "test-ref",
            WorkerImage = "test-image",
            DebugExec = debugExec,
        };
    }
}
