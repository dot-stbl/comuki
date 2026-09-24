using System.Diagnostics;

namespace Comuki.AgentEval.Pi;

/// <summary>
/// Result of <see cref="RealPiInstaller.InstallAsync"/>: either the
/// <c>bun add</c> succeeded and <see cref="ExecutablePath"/> points to
/// the installed <c>pi.exe</c> / <c>pi</c> shim, or it failed and
/// <see cref="Reason"/> explains why (bun missing, vendored tgz missing,
/// <c>bun add</c> non-zero exit, shim not found). The installer never
/// throws for any of these expected failure modes — callers branch on
/// <see cref="Succeeded"/> and surface <see cref="Reason"/> to the user.
/// </summary>
/// <param name="Succeeded">True iff a usable <c>pi</c> executable path is set.</param>
/// <param name="ExecutablePath">Absolute path to the installed <c>pi</c> shim; empty when <paramref name="Succeeded"/> is false.</param>
/// <param name="Reason">Human-readable failure reason; empty on success.</param>
public sealed record PiInstallResult(bool Succeeded, string ExecutablePath, string Reason);

/// <summary>
/// Installs the real, vendored <c>pi</c> binary
/// (tgz at <c>deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz</c>)
/// into a scratch temp directory via <c>bun add [tgz]</c>.
/// Re-derived from the integration suite's <c>RealPiInstallation</c>
/// (read-only reference) so this tool does not need a ProjectReference
/// to <c>Comuki.EndToEnd.AgentLoop</c> (which is xUnit-only and drags in
/// Postgres/Testcontainers, neither of which this standalone CLI tool
/// needs).
/// </summary>
/// <remarks>
/// On Windows, bun's generated <c>pi.exe</c> is a real native
/// trampoline executable (not a <c>.cmd</c> shim), so
/// <c>Process.Start(UseShellExecute=false)</c> hands the <c>ArgumentList</c>
/// bytes through to real pi's <c>argv</c> unchanged. Same reasoning as
/// <c>RealPiInstallation.RunBunAddAsync</c>'s own doc comment.
/// </remarks>
public static class RealPiInstaller
{
    private const string VendoredTgzRelativePath = "deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz";

    /// <summary>
    /// Installs the vendored <c>pi</c> tgz into a fresh scratch temp dir
    /// and resolves <c>node_modules/.bin/pi.exe</c> / <c>pi</c>.
    /// </summary>
    /// <param name="ct">Cancellation forwarded to the underlying <c>bun add</c> child process.</param>
    public static async Task<PiInstallResult> InstallAsync(CancellationToken ct)
    {
        var bunExe = LocateOnPath("bun");
        if (bunExe is null)
        {
            return new PiInstallResult(false, string.Empty, "bun is not on PATH — install bun (https://bun.sh) to enable the real-pi path");
        }

        var repoRoot = LocateRepoRoot();
        if (repoRoot is null)
        {
            return new PiInstallResult(false, string.Empty, "could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
        }

        var tgzPath = Path.Combine(repoRoot, VendoredTgzRelativePath.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(tgzPath))
        {
            return new PiInstallResult(false, string.Empty, $"vendored pi tarball not found at '{tgzPath}'");
        }

        string installDir;
        try
        {
            installDir = Path.Combine(Path.GetTempPath(), "comuki-agent-eval-pi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installDir);
            await File.WriteAllTextAsync(
                Path.Combine(installDir, "package.json"),
                /*lang=json,strict*/ """{"name":"comuki-agent-eval-install","private":true,"version":"0.0.0"}""",
                ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new PiInstallResult(false, string.Empty, $"could not create scratch install dir: {exception.Message}");
        }

        try
        {
            await RunBunAddAsync(bunExe, installDir, tgzPath, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new PiInstallResult(false, string.Empty, $"bun add failed: {exception.Message}");
        }

        var binaryName = OperatingSystem.IsWindows() ? "pi.exe" : "pi";
        var executablePath = Path.Combine(installDir, "node_modules", ".bin", binaryName);
        return File.Exists(executablePath)
            ? new PiInstallResult(true, executablePath, string.Empty)
            : new PiInstallResult(false, string.Empty, $"bun add completed but no '{binaryName}' shim was found at '{executablePath}' — pi's package.json bin entry may have changed; verify the vendored tgz version");
    }

    private static async Task RunBunAddAsync(string bunExecutable, string workingDirectory, string tgzPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(bunExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("add");
        startInfo.ArgumentList.Add(tgzPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{bunExecutable} add' — process is null");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{bunExecutable} add {tgzPath}' exited {process.ExitCode}.\nstdout: {await stdoutTask.ConfigureAwait(false)}\nstderr: {await stderrTask.ConfigureAwait(false)}");
        }
    }

    private static string? LocateOnPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? executableName + ".exe" : executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the call — fine.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best-effort teardown, same tolerance as the rest of the
            // real-pi tooling.
        }
    }
}
