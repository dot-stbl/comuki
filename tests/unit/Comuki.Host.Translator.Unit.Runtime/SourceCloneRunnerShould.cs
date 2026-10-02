using System.Diagnostics;
using System.Text;
using Comuki.Host.Translator.Execution.Clone;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Workspace source-clone behaviour (harden-pi-worker-sandbox 4.3): a
/// missing URL fails the item, a credential rides in a throwaway
/// <c>GIT_CONFIG_GLOBAL</c> and never in plaintext argv, a leftover clone
/// from a warm slot is wiped, and the real <see cref="SourceCloneProcessRunner"/>
/// can clone a filesystem-local repo.
/// </summary>
public sealed class SourceCloneRunnerShould
{
    [Fact(DisplayName = "Given a null SourceGitUrl, when RunAsync runs, then outcome is MissingUrl and no clone is spawned")]
    public async Task MissingUrlFailsWithoutSpawningAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);

            var outcome = await runner.RunAsync(workingDirectory, sourceGitUrl: null, sourceGitRef: null, gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.MissingUrl);
            outcome.RepositoryDirectory.ShouldBe(string.Empty);
            outcome.Reason.ShouldNotBeNullOrWhiteSpace();
            fake.Calls.ShouldBeEmpty();
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a whitespace SourceGitUrl, when RunAsync runs, then outcome is MissingUrl and no clone is spawned")]
    public async Task WhitespaceUrlFailsWithoutSpawningAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);

            var outcome = await runner.RunAsync(workingDirectory, sourceGitUrl: "   ", sourceGitRef: null, gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.MissingUrl);
            fake.Calls.ShouldBeEmpty();
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a credential, when RunAsync runs, then GIT_CONFIG_GLOBAL points at a file with the base64 Basic header and the file is deleted afterwards")]
    public async Task CredentialRidesInThrowawayConfigAndIsDeletedAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://github.com/example/private.git";

            var outcome = await runner.RunAsync(workingDirectory, url, sourceGitRef: null, gitCredential: "RAW_TOKEN", TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.Succeeded);
            outcome.RepositoryDirectory.ShouldBe(Path.Combine(workingDirectory, SourceCloneRunner.RepositoryDirectoryName));
            fake.Calls.Count.ShouldBe(1);
            var call = fake.Calls[0];

            call.Executable.ShouldBe("git");
            call.Arguments.ShouldNotContain(static arg => arg.Contains("RAW_TOKEN", StringComparison.Ordinal));
            call.Environment.ShouldNotBeNull();
            call.Environment.ShouldContainKey("GIT_CONFIG_GLOBAL");
            var configPath = call.Environment!["GIT_CONFIG_GLOBAL"];
            File.Exists(configPath).ShouldBeFalse();
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a credential, when RunAsync runs, then the throwaway config file contains Authorization: Basic and the base64 of x-access-token:TOKEN")]
    public async Task ThrowawayConfigContainsBasicHeaderWithTokenAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        var preservedConfigPath = Path.Combine(Path.GetTempPath(), $"comuki-git-test-preserve-{Guid.NewGuid():N}.config");
        try
        {
            var fake = new FakeSourceCloneProcessRunner { ConfigCapturePath = preservedConfigPath };
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://github.com/example/private.git";
            var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:RAW_TOKEN"));

            await runner.RunAsync(workingDirectory, url, sourceGitRef: null, gitCredential: "RAW_TOKEN", TestContext.Current.CancellationToken);

            File.Exists(preservedConfigPath).ShouldBeTrue();
            var content = await File.ReadAllTextAsync(preservedConfigPath, TestContext.Current.CancellationToken);
            content.ShouldContain("extraHeader = \"Authorization: Basic");
            content.ShouldContain(expectedBase64);
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
            if (File.Exists(preservedConfigPath))
            {
                File.Delete(preservedConfigPath);
            }
        }
    }

    [Fact(DisplayName = "Given no credential, when RunAsync runs, then environment is null and arguments are exactly clone --depth 1 url target")]
    public async Task NoCredentialKeepsEnvironmentEmptyAndBuildsCloneArgvAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://github.com/example/public.git";
            var expectedTarget = Path.Combine(workingDirectory, SourceCloneRunner.RepositoryDirectoryName);

            var outcome = await runner.RunAsync(workingDirectory, url, sourceGitRef: null, gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.Succeeded);
            var call = fake.Calls[0];
            call.Environment.ShouldBeNull();
            call.Arguments.ShouldBe(["clone", "--depth", "1", url, expectedTarget]);
            call.WorkingDirectory.ShouldBe(workingDirectory);
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a ref, when RunAsync runs, then arguments include --branch ref before url and target")]
    public async Task RefAddsBranchArgumentAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://github.com/example/public.git";
            var expectedTarget = Path.Combine(workingDirectory, SourceCloneRunner.RepositoryDirectoryName);

            var outcome = await runner.RunAsync(workingDirectory, url, sourceGitRef: "release/1.x", gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.Succeeded);
            fake.Calls[0].Arguments.ShouldBe(["clone", "--depth", "1", "--branch", "release/1.x", url, expectedTarget]);
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given an authentication failure (exit 128 + stderr), when RunAsync runs, then outcome is CloneFailed with stderr line in the Reason")]
    public async Task AuthenticationFailureReportsStderrLineAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var fake = new FakeSourceCloneProcessRunner
            {
                Result = new SourceCloneStepResult(ExitCode: 128, LaunchFailureDetail: null, StandardError: "fatal: Authentication failed for 'https://example.com/x.git'\n"),
            };
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://example.com/private.git";

            var outcome = await runner.RunAsync(workingDirectory, url, sourceGitRef: null, gitCredential: "RAW_TOKEN", TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.CloneFailed);
            outcome.RepositoryDirectory.ShouldBe(string.Empty);
            outcome.Reason.ShouldContain("fatal: Authentication failed for 'https://example.com/x.git'");
            outcome.Reason.ShouldNotContain("\n");
            var configPath = fake.Calls[0].Environment!["GIT_CONFIG_GLOBAL"];
            File.Exists(configPath).ShouldBeFalse();
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a leftover stray file at <wd>/source, when RunAsync runs successfully, then the stray content is gone")]
    public async Task LeftoverStrayFileIsWipedBeforeCloneAsync()
    {
        var workingDirectory = SourceCloneRunnerTestHelpers.NewWorkingDirectory();
        try
        {
            var target = Path.Combine(workingDirectory, SourceCloneRunner.RepositoryDirectoryName);
            Directory.CreateDirectory(target);
            var strayFile = Path.Combine(target, "stray-from-previous-item.txt");
            await File.WriteAllTextAsync(strayFile, "left-over from previous run", TestContext.Current.CancellationToken);

            var fake = new FakeSourceCloneProcessRunner();
            var runner = new SourceCloneRunner(fake, NullLogger<SourceCloneRunner>.Instance);
            var url = "https://github.com/example/public.git";

            var outcome = await runner.RunAsync(workingDirectory, url, sourceGitRef: null, gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.Succeeded);
            File.Exists(strayFile).ShouldBeFalse();
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }

    [Fact(DisplayName = "Given a filesystem-local git repo as SourceGitUrl, when RunAsync runs with the real process runner, then <wd>/source contains the committed file")]
    public async Task RealProcessRunnerClonesFilesystemRepoAsync()
    {
        if (!SourceCloneRunnerTestHelpers.GitOnPath())
        {
            return;
        }

        var sourceRepo = Path.Combine(Path.GetTempPath(), $"comuki-clone-source-{Guid.NewGuid():N}");
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"comuki-clone-wd-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sourceRepo);
            // The clone child runs with this as its cwd — Process.Start
            // fails on a non-existent working directory.
            Directory.CreateDirectory(workingDirectory);
            SourceCloneRunnerTestHelpers.RunGit(sourceRepo, "init --initial-branch=main");
            SourceCloneRunnerTestHelpers.RunGit(sourceRepo, "config user.email test@example.com");
            SourceCloneRunnerTestHelpers.RunGit(sourceRepo, "config user.name Test");
            var readmePath = Path.Combine(sourceRepo, "README.md");
            await File.WriteAllTextAsync(readmePath, "source-clone-fixture", TestContext.Current.CancellationToken);
            SourceCloneRunnerTestHelpers.RunGit(sourceRepo, "add README.md");
            SourceCloneRunnerTestHelpers.RunGit(sourceRepo, "commit -m \"fixture\"");

            var runner = new SourceCloneRunner(new SourceCloneProcessRunner(), NullLogger<SourceCloneRunner>.Instance);

            var outcome = await runner.RunAsync(workingDirectory, sourceRepo, sourceGitRef: null, gitCredential: null, TestContext.Current.CancellationToken);

            outcome.Kind.ShouldBe(SourceCloneOutcomeKind.Succeeded);
            var clonedReadme = Path.Combine(outcome.RepositoryDirectory, "README.md");
            File.Exists(clonedReadme).ShouldBeTrue();
            (await File.ReadAllTextAsync(clonedReadme, TestContext.Current.CancellationToken))
                .ShouldBe("source-clone-fixture");
        }
        finally
        {
            SourceCloneRunnerTestHelpers.DeleteDirectory(sourceRepo);
            SourceCloneRunnerTestHelpers.DeleteDirectory(workingDirectory);
        }
    }
}

/// <summary>
/// File-scoped helpers for <see cref="SourceCloneRunnerShould"/> — temp
/// directory provisioning, real-git probe and local-repo fixture
/// (kept out of the test class per class-layout §1a — no private methods).
/// </summary>
file static class SourceCloneRunnerTestHelpers
{
    public static string NewWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comuki-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // git marks .git/objects files read-only on Windows — plain
        // Directory.Delete(recursive) throws UnauthorizedAccessException.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    public static bool GitOnPath()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            })!;
            probe.WaitForExit(500);
            return probe.ExitCode is 0 or 1;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static void RunGit(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} in {workingDirectory} exited {process.ExitCode}");
        }
    }
}

/// <summary>
/// Hand-rolled fake: captures every <see cref="ISourceCloneProcessRunner.RunAsync"/>
/// invocation and returns a configured <see cref="SourceCloneStepResult"/>.
/// The real source-clone test asserts credential-file contents, so the
/// fake can be told to keep a copy of the throwaway config before the
/// runner deletes it.
/// </summary>
file sealed class FakeSourceCloneProcessRunner : ISourceCloneProcessRunner
{
    public List<FakeSourceCloneCall> Calls { get; } = [];

    public SourceCloneStepResult Result { get; init; } = new SourceCloneStepResult(ExitCode: 0, LaunchFailureDetail: null, StandardError: string.Empty);

    public string? ConfigCapturePath { get; init; }

    public Task<SourceCloneStepResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (environment is { } env
            && ConfigCapturePath is { } capturePath
            && env.TryGetValue("GIT_CONFIG_GLOBAL", out var sourcePath)
            && File.Exists(sourcePath))
        {
            File.Copy(sourcePath, capturePath, overwrite: true);
        }

        Calls.Add(new FakeSourceCloneCall(executable, [.. arguments], environment is null ? null : new Dictionary<string, string>(environment), workingDirectory));

        return Task.FromResult(Result);
    }
}

/// <summary>Snapshot of one fake invocation, suitable for direct assertions.</summary>
file sealed record FakeSourceCloneCall(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment,
    string? WorkingDirectory);
