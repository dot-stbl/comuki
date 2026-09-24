using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Application.Ports;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Modules.Verify.Infrastructure.Sync;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// GenericCommandVerifierWorker polling cycle: a claimed Pending run is
/// launched and stamped Green/Red from the exit code, one failing run
/// does not poison the rest of the batch, and the worker is a
/// documented no-op while disabled.
/// </summary>
public sealed class GenericCommandVerifierWorkerShould
{
    private static readonly DateTimeOffset anchorTime = DateTimeOffset.Parse(
        "2026-09-23T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact(DisplayName = "Given a claimed run whose exit code matches, when the worker polls once, then it is stamped Green")]
    public async Task StampGreenOnMatchingExitCodeAsync()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], expectedExitCode: 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        runner.RunAsync("dotnet", Arg.Is<IReadOnlyList<string>>(static args => args.SequenceEqual(new[] { "--version" })), null, Arg.Any<CancellationToken>())
            .Returns(new GenericCommandRunResult(0, "[out] 10.0.303\n", null));

        var worker = NewWorker([run], runner, out _);

        var processed = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        processed.ShouldBe(1);
        run.Status.ShouldBe(GenericCommandStatus.Green);
    }

    [Fact(DisplayName = "Given a claimed run whose exit code does not match, when the worker polls once, then it is stamped Red")]
    public async Task StampRedOnMismatchedExitCodeAsync()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["build"], expectedExitCode: 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GenericCommandRunResult(1, "[err] boom\n", null));

        var worker = NewWorker([run], runner, out _);

        await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        run.Status.ShouldBe(GenericCommandStatus.Red);
        run.ActualExitCode.ShouldBe(1);
    }

    [Fact(DisplayName = "Given a runner that could not launch, when the worker polls once, then the run is stamped Red with the failure detail")]
    public async Task StampRedOnLaunchFailureAsync()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "nope", [], expectedExitCode: 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GenericCommandRunResult(null, string.Empty, "launch failed: file not found"));

        var worker = NewWorker([run], runner, out _);

        await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        run.Status.ShouldBe(GenericCommandStatus.Red);
        run.OutputLog.ShouldBe("launch failed: file not found");
    }

    [Fact(DisplayName = "Given two claimed runs, when the worker polls once, then both are processed in the same cycle")]
    public async Task ProcessesMultipleRunsInOneCycleAsync()
    {
        var first = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        var second = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--info"], 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GenericCommandRunResult(0, string.Empty, null));

        var worker = NewWorker([first, second], runner, out _);

        var processed = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        processed.ShouldBe(2);
    }

    [Fact(DisplayName = "Given a runner that throws for one run, when the worker polls, then the other run still completes")]
    public async Task PerRunIsolationAsync()
    {
        var failing = GenericCommandRun.Create(null, string.Empty, "dotnet", ["fails"], 0, anchorTime);
        var succeeding = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        runner.RunAsync("dotnet", Arg.Is<IReadOnlyList<string>>(static args => args.SequenceEqual(new[] { "fails" })), null, Arg.Any<CancellationToken>())
            .Returns<Task<GenericCommandRunResult>>(static _ => throw new HttpRequestException("upstream gone"));
        runner.RunAsync("dotnet", Arg.Is<IReadOnlyList<string>>(static args => args.SequenceEqual(new[] { "--version" })), null, Arg.Any<CancellationToken>())
            .Returns(new GenericCommandRunResult(0, string.Empty, null));

        var worker = NewWorker([failing, succeeding], runner, out _);

        var processed = await worker.PollOnceAsync(TestContext.Current.CancellationToken);

        // boundary: the worker's own per-run isolation — one failing
        // runner call leaves that run in Running (already stamped before
        // the runner was invoked) and never stops the rest of the batch.
        processed.ShouldBe(1);
        failing.Status.ShouldBe(GenericCommandStatus.Running);
        succeeding.Status.ShouldBe(GenericCommandStatus.Green);
    }

    [Fact(DisplayName = "Given the worker is disabled, when ExecuteAsync runs a cycle, then it is a no-op that never touches the store")]
    public async Task NoOpWhenDisabledAsync()
    {
        var run = GenericCommandRun.Create(null, string.Empty, "dotnet", ["--version"], 0, anchorTime);
        var runner = Substitute.For<IGenericCommandRunner>();
        var worker = NewWorker([run], runner, out var store, enabled: false);

        var result = await worker.ExecuteAsync(
            new WorkerContext(Substitute.For<IServiceProvider>(), TimeProvider.System, NullLogger.Instance),
            TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        await store.DidNotReceive().ClaimPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await runner.DidNotReceive().RunAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a worker, when its identity is read, then it declares a kebab-case name and an interval schedule from options")]
    public void DeclareNameAndSchedule()
    {
        var worker = NewWorker([], Substitute.For<IGenericCommandRunner>(), out _, pollInterval: TimeSpan.FromSeconds(45));

        worker.Name.ShouldBe("verify-generic-command");
        worker.Schedule.ShouldBeOfType<WorkerSchedule.IntervalWorkerSchedule>();
        ((WorkerSchedule.IntervalWorkerSchedule)worker.Schedule).PollInterval.ShouldBe(TimeSpan.FromSeconds(45));
    }

    private static GenericCommandVerifierWorker NewWorker(
        IReadOnlyList<GenericCommandRun> claimed,
        IGenericCommandRunner runner,
        out IGenericCommandStore store,
        bool enabled = true,
        TimeSpan? pollInterval = null)
    {
        var capturedStore = Substitute.For<IGenericCommandStore>();
        capturedStore.ClaimPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(claimed);
        store = capturedStore;

        var services = new ServiceCollection();
        services.AddScoped(_ => capturedStore);
        services.AddScoped(_ => runner);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        scopeAccessor.AsSystem(Arg.Any<string>()).Returns(new SystemScope());

        return new GenericCommandVerifierWorker(
            TimeProvider.System,
            scopeFactory,
            scopeAccessor,
            Options.Create(new VerifyOptions { Enabled = enabled, PollInterval = pollInterval ?? TimeSpan.FromSeconds(30) }),
            NullLogger<GenericCommandVerifierWorker>.Instance);
    }

    private sealed class SystemScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
