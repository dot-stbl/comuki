using System.Diagnostics;

namespace Comuki.Host.Translator.Execution.Publish.Git;

/// <summary>
/// Production <see cref="ISourceGitPublisher"/>: one <c>git</c> child
/// process per call — <c>status --porcelain</c>, <c>rev-parse</c>,
/// <c>checkout -B</c>, and <c>push -u origin</c>. When the claim carried a
/// credential, the throwaway config path is handed to git via the
/// <c>GIT_CONFIG_GLOBAL</c> environment variable of that one child process
/// — the credential value itself never appears in the Translator's own
/// environment, on git's command line, or in logs. Non-zero exits (and a
/// missing git binary) surface as
/// <see cref="WorkspacePublishException"/> with
/// <see cref="WorkspacePublishException.PushFailedCode"/>; <c>rev-parse</c>
/// alone returns <c>null</c> on a missing spec so a fresh clone with no
/// <c>origin/HEAD</c> still pushes. Cancellation kills the child by its
/// own handle, never by a blanket process kill.
/// </summary>
/// <param name="logger">Structured logger; the stderr excerpt never contains the credential (it rides in a header).</param>
public sealed class GitSourceGitPublisher(ILogger<GitSourceGitPublisher> logger) : ISourceGitPublisher
{
    /// <inheritdoc />
    public async Task<string> ReadStatusAsync(SourceGitPublish publish, CancellationToken cancellationToken = default)
    {
        var result = await GitSourceGitPublisherHelpers.RunAsync(
            publish,
            logger,
            cancellationToken,
            "status",
            "--porcelain");
        return result.ExitCode != 0
            ? throw GitSourceGitPublisherHelpers.PushFailed(result.ExitCode, result.Stderr)
            : result.Stdout;
    }

    /// <inheritdoc />
    public async Task<string?> RevParseAsync(SourceGitPublish publish, string spec, CancellationToken cancellationToken = default)
    {
        var result = await GitSourceGitPublisherHelpers.RunAsync(
            publish,
            logger,
            cancellationToken,
            "rev-parse",
            spec);
        return result.ExitCode == 0 ? result.Stdout.Trim() : null;
    }

    /// <inheritdoc />
    public async Task CheckoutBranchAsync(SourceGitPublish publish, CancellationToken cancellationToken = default)
    {
        var result = await GitSourceGitPublisherHelpers.RunAsync(
            publish,
            logger,
            cancellationToken,
            "checkout",
            "-B",
            publish.Branch);
        if (result.ExitCode != 0)
        {
            throw GitSourceGitPublisherHelpers.PushFailed(result.ExitCode, result.Stderr);
        }
    }

    /// <inheritdoc />
    public async Task PushAsync(SourceGitPublish publish, CancellationToken cancellationToken = default)
    {
        var result = await GitSourceGitPublisherHelpers.RunAsync(
            publish,
            logger,
            cancellationToken,
            "push",
            "-u",
            "origin",
            publish.Branch);
        if (result.ExitCode != 0)
        {
            throw GitSourceGitPublisherHelpers.PushFailed(result.ExitCode, result.Stderr);
        }
    }
}

/// <summary>
/// File-scoped helpers of the one-shot publish git calls: shared
/// <c>git</c> child process launcher, bounded stderr excerpts, and
/// cancellation teardown that kills by handle, never a blanket process
/// kill.
/// </summary>
file static class GitSourceGitPublisherHelpers
{
    private const string GitExecutable = "git";

    private const string GitConfigGlobalVariable = "GIT_CONFIG_GLOBAL";

    private const int StderrExcerptLength = 500;

    /// <summary>Captured stdout/stderr of one git child process.</summary>
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Spawns <c>git</c> with the supplied subcommand arguments as a single
    /// child of <see cref="SourceGitPublish.WorkingDirectory"/>, optionally
    /// with <c>GIT_CONFIG_GLOBAL</c> pointing at the throwaway credential
    /// file. Cancellation kills the child handle; never a blanket process kill.
    /// </summary>
    public static async Task<Result> RunAsync(
        SourceGitPublish publish,
        ILogger logger,
        CancellationToken cancellationToken,
        params string[] gitArguments)
    {
        var startInfo = new ProcessStartInfo(GitExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = publish.WorkingDirectory,
        };
        foreach (var argument in gitArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (publish.CredentialConfigPath is { } credentialConfigPath)
        {
            startInfo.Environment[GitConfigGlobalVariable] = credentialConfigPath;
        }

        using var process = Process.Start(startInfo)
            ?? throw new WorkspacePublishException(
                WorkspacePublishException.PushFailedCode,
                $"failed to start '{GitExecutable}' — is git installed in the worker image?");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Kill(process, logger);
            throw;
        }

        var stderr = await stderrTask;
        if (stderr.Length > 0)
        {
            logger.LogDebug(
                "git {Args} stderr: {Stderr}",
                string.Join(' ', gitArguments),
                Excerpt(stderr));
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        return new Result(process.ExitCode, stdout, Excerpt(stderr));
    }

    /// <summary>Bounded stderr excerpt — a full remote error can be pages long.</summary>
    public static string Excerpt(string stderr)
    {
        return stderr.Length <= StderrExcerptLength ? stderr.Trim() : stderr[..StderrExcerptLength].Trim();
    }

    /// <summary>Typed failure factory — never carries the credential value (the credential lives in a header git itself redacts).</summary>
    public static WorkspacePublishException PushFailed(int exitCode, string stderr)
    {
        return new WorkspacePublishException(
            WorkspacePublishException.PushFailedCode,
            $"git exited with code {exitCode}: {Excerpt(stderr)}");
    }

    public static void Kill(Process process, ILogger logger)
    {
        logger.LogWarning("Cancelling git child (PID {Pid})", process.Id);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            logger.LogWarning(exception, "failed to kill git process {Pid}", process.Id);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "git process {Pid} already exited", process.Id);
        }
    }
}
