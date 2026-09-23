using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// <see cref="GenericCommandProcessRunner"/> against real child processes
/// — <c>dotnet</c> is guaranteed present in every dev/CI environment that
/// can build this solution, so these tests need no OS-specific fixture
/// beyond the timeout case (which needs a real "still running" process
/// and so picks the OS-native sleep command).
/// </summary>
public sealed class GenericCommandProcessRunnerShould
{
    [Fact(DisplayName = "Given a valid executable and arguments, when RunAsync is called, then it captures the exit code and output")]
    public async Task ReturnExitCodeAndOutputOnSuccessAsync()
    {
        var runner = NewRunner();

        var result = await runner.RunAsync("dotnet", ["--version"], workingDirectory: null, TestContext.Current.CancellationToken);

        result.LaunchFailed.ShouldBeFalse();
        result.ExitCode.ShouldBe(0);
        result.OutputLog.ShouldContain("[out]");
    }

    [Fact(DisplayName = "Given a command that exits non-zero, when RunAsync is called, then the exit code is reported (not swallowed)")]
    public async Task ReturnNonZeroExitCodeAsync()
    {
        var runner = NewRunner();

        var result = await runner.RunAsync("dotnet", ["verify-generic-command-nonexistent-subcommand"], workingDirectory: null, TestContext.Current.CancellationToken);

        result.LaunchFailed.ShouldBeFalse();
        result.ExitCode.ShouldNotBe(0);
    }

    [Fact(DisplayName = "Given an executable string containing shell metacharacters, when RunAsync is called, then it fails to launch instead of being shell-interpreted")]
    public async Task NotShellInterpretTheExecutableNameAsync()
    {
        // UseShellExecute=false means the FileName is looked up literally
        // as a file/PATH entry — a string like this is never a real
        // executable, so a shell would need to have parsed it for
        // "dotnet --version" to actually run. Failing to launch proves no
        // shell ever saw it.
        var runner = NewRunner();

        var result = await runner.RunAsync(
            "dotnet --version; echo pwned",
            arguments: [],
            workingDirectory: null,
            TestContext.Current.CancellationToken);

        result.LaunchFailed.ShouldBeTrue();
        result.LaunchFailureDetail.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a command that outlives the configured timeout, when RunAsync is called, then it is killed and reported as a launch failure")]
    public async Task KillAndReportTimeoutAsync()
    {
        var runner = NewRunner(runTimeout: TimeSpan.FromMilliseconds(300));
        var (executable, arguments) = SleepCommand(seconds: 5);

        var result = await runner.RunAsync(executable, arguments, workingDirectory: null, TestContext.Current.CancellationToken);

        result.LaunchFailed.ShouldBeTrue();
        result.LaunchFailureDetail.ShouldNotBeNull();
        result.LaunchFailureDetail.ShouldContain("timeout");
    }

    private static GenericCommandProcessRunner NewRunner(TimeSpan? runTimeout = null)
    {
        return new GenericCommandProcessRunner(
            Options.Create(new VerifyOptions
            {
                RunTimeout = runTimeout ?? TimeSpan.FromSeconds(30),
                OutputLogCharCap = 65_536,
            }),
            NullLogger<GenericCommandProcessRunner>.Instance);
    }

    private static (string Executable, string[] Arguments) SleepCommand(int seconds)
    {
        return OperatingSystem.IsWindows()
            ? ("powershell", ["-NoProfile", "-NonInteractive", "-Command", $"Start-Sleep -Seconds {seconds}"])
            : ("sleep", [seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }
}
