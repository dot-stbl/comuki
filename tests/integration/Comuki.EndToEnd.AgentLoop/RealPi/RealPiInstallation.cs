using System.Diagnostics;
using Xunit;

namespace Comuki.EndToEnd.AgentLoop.RealPi;

/// <summary>
/// Installs the real, vendored <c>pi</c> binary
/// (<c>deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz</c>)
/// into a scratch temp directory once per test collection, via
/// <c>bun add &lt;tgz&gt;</c> — WS7 task 7.1's "point the real pi binary" half.
/// </summary>
/// <remarks>
/// <para>
/// <c>bun add</c>, not <c>npm install</c>: both work (verified manually —
/// npm resolves the same npm-registry dependency graph fine), but on
/// Windows npm's generated <c>node_modules/.bin/pi.cmd</c> is a batch-file
/// shim, and <see cref="Host.Translator.Runtime.PiRunner"/> spawns
/// <c>PiExecutable</c> directly via <c>Process.Start</c> with
/// <c>UseShellExecute = false</c> — .NET launches a <c>.cmd</c> through
/// <c>cmd.exe</c> automatically, but cmd.exe's OWN command-line grammar
/// (<c>&amp;</c>/<c>|</c>/<c>&lt;</c>/<c>&gt;</c>/<c>^</c> as shell
/// metacharacters) then re-parses the forwarded <c>-p BRIEF</c> argument
/// before <c>%*</c> substitution — verified empirically to corrupt or
/// outright reject a realistic brief (a ticket body containing any of
/// those characters). Bun's Windows package-manager shims are real native
/// trampoline executables (<c>pi.exe</c>, ~8 KB), not batch files —
/// CreateProcess launches them directly with no shell re-parsing step, so
/// the exact bytes .NET's <c>ArgumentList</c> encodes reach real pi's
/// <c>argv</c> unchanged (verified with a brief containing embedded
/// quotes, <c>&amp;</c>, <c>&lt;&gt;^%</c> and non-ASCII text). This is
/// also why a real container (Linux, POSIX <c>exec</c>, no shell
/// involved either way) never hits this problem — it is Windows-only, and
/// specific to the in-process (non-container) T2b harness this class
/// serves.
/// </para>
/// <para>
/// No node_modules/dependency network access is needed beyond this one
/// install: pi's own <c>dist/bundle/cli.js</c> is a fully bundled,
/// self-contained JS payload (esbuild-bundled at publish time) — the only
/// thing <c>bun add</c> resolves from the registry is pi's OWN declared
/// dependencies (its sibling <c>@earendil-works/*</c> packages plus
/// third-party libs), not anything from this repo.
/// </para>
/// </remarks>
public sealed class RealPiInstallation : IAsyncLifetime
{
    private string? installDirectory;

    /// <summary>Absolute path to the installed <c>pi</c> executable — a real native binary on every OS bun supports.</summary>
    public string ExecutablePath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        installDirectory = Path.Combine(Path.GetTempPath(), "comuki-real-pi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(installDirectory, "package.json"),
            /*lang=json,strict*/ """{"name":"comuki-real-pi-install","private":true,"version":"0.0.0"}""",
            cancellationToken);

        await RunBunAddAsync(installDirectory, ResolveVendoredTgzPath(), cancellationToken);

        var binaryName = OperatingSystem.IsWindows() ? "pi.exe" : "pi";
        var executablePath = Path.Combine(installDirectory, "node_modules", ".bin", binaryName);
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                $"'bun add' completed but no '{binaryName}' shim was found at '{executablePath}' — pi's package.json "
                    + "bin entry may have changed; check tests/tools/Comuki.AgentTest.Runner's vendored tgz version.");
        }

        ExecutablePath = executablePath;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (installDirectory is not null && Directory.Exists(installDirectory))
        {
            try
            {
                Directory.Delete(installDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup — a lingering handle (e.g. a slow
                // antivirus scan on Windows) must not fail the test run.
            }
            catch (UnauthorizedAccessException)
            {
                // Same tolerance as the IOException case above.
            }
        }

        return ValueTask.CompletedTask;
    }

    private static async Task RunBunAddAsync(string workingDirectory, string tgzPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("bun")
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
            ?? throw new InvalidOperationException("failed to start 'bun add' — is bun installed and on PATH?");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // cross-platform.md §8: kill the tree on cancellation — bun's own
            // child processes (its own worker threads/subprocess helpers)
            // must not linger past this call's timeout/cancellation.
            KillProcessTree(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'bun add {tgzPath}' exited {process.ExitCode}.\nstdout: {await stdoutTask}\nstderr: {await stderrTask}");
        }
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
            // Best-effort teardown, same tolerance as PiRunner's own
            // PiProcessHelpers.TearDownAsync for this exact race.
        }
    }

    private static string ResolveVendoredTgzPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        var tgzPath = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory),
            "deploy", "hybrid", "vendor", "earendil-works-pi-coding-agent-0.85.1.tgz");
        return File.Exists(tgzPath)
            ? tgzPath
            : throw new InvalidOperationException($"vendored pi tarball not found at '{tgzPath}'");
    }
}
