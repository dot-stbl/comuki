using System.Diagnostics;
using Comuki.AgentTest.Runner.Execution;
using Comuki.AgentTest.Runner.Scenarios;

namespace Comuki.AgentEval.Judges;

/// <summary>
/// The five deterministic judges a WS10 corpus entry is checked
/// against, after a real <c>pi</c> run finishes. Each judge is a
/// pure/offline function except <see cref="EvaluateTestsPassAsync"/>,
/// which spawns the entry's declared <c>testCommand</c> as a child
/// process. None of the judges throw past their own boundary — every
/// failure mode becomes a <see cref="DeterministicVerdict"/> with a
/// human-readable <c>Message</c>.
/// </summary>
public static class DeterministicJudges
{
    /// <summary>
    /// Diff-applies judge. Walks every file under <paramref name="workingDirectory"/>,
    /// compares bytes against the same relative path under
    /// <paramref name="pristineFixtureDirectory"/> (a missing
    /// pristine file counts as "new in the run" = changed); passes iff
    /// at least one file differs. When <paramref name="baseDiffAssertion"/>
    /// is non-null, also runs the EXISTING
    /// <c>Comuki.AgentTest.Runner.Execution.DiffAssertionEvaluator.EvaluateAsync</c>
    /// against the same working directory and folds any failure into this
    /// verdict — reuse, do not reimplement, the substring check.
    /// </summary>
    public static async Task<DeterministicVerdict> EvaluateDiffAppliesAsync(
        string workingDirectory,
        string pristineFixtureDirectory,
        DiffAssertion? baseDiffAssertion,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return new DeterministicVerdict
            {
                JudgeName = "diff-applies",
                Passed = false,
                Message = $"working directory does not exist: {workingDirectory}",
            };
        }

        if (!Directory.Exists(pristineFixtureDirectory))
        {
            return new DeterministicVerdict
            {
                JudgeName = "diff-applies",
                Passed = false,
                Message = $"pristine fixture directory does not exist: {pristineFixtureDirectory}",
            };
        }

        var changedFiles = EnumerateChangedFiles(workingDirectory, pristineFixtureDirectory);
        var changedCount = changedFiles.Count;
        var changedRelativePaths = changedFiles
            .Select(changedFile => Path.GetRelativePath(workingDirectory, changedFile).Replace('\\', '/'))
            .ToList();

        if (baseDiffAssertion is not null)
        {
            var substringFailure = await DiffAssertionEvaluator
                .EvaluateAsync(baseDiffAssertion, workingDirectory, cancellationToken)
                ;
            if (substringFailure is not null)
            {
                return new DeterministicVerdict
                {
                    JudgeName = "diff-applies",
                    Passed = false,
                    Message = $"{substringFailure} (changed files: {string.Join(", ", changedRelativePaths)})",
                };
            }
        }

        return new DeterministicVerdict
        {
            JudgeName = "diff-applies",
            Passed = changedCount > 0,
            Message = changedCount == 0
                ? "no files in the working directory differ from the pristine fixture"
                : $"changed {changedCount} file(s): {string.Join(", ", changedRelativePaths)}",
        };
    }

    /// <summary>
    /// Allowed-files judge. Returns not-applicable when
    /// <paramref name="allowedFiles"/> is null/empty; otherwise passes
    /// iff every entry of <paramref name="touchedFiles"/>
    /// (repo-relative, forward-slash normalized) appears in
    /// <paramref name="allowedFiles"/>. The message names the first
    /// disallowed file when failing.
    /// </summary>
    public static DeterministicVerdict EvaluateFilesTouchedWithinAllowedSet(
        IReadOnlyList<string> touchedFiles,
        IReadOnlyList<string> allowedFiles)
    {
        if (allowedFiles.Count == 0)
        {
            return NotApplicable("files-within-allowed-set", "no allowedFiles declared");
        }

        var allowed = new HashSet<string>(allowedFiles, StringComparer.Ordinal);

        foreach (var touched in touchedFiles)
        {
            if (!allowed.Contains(touched))
            {
                return new DeterministicVerdict
                {
                    JudgeName = "files-within-allowed-set",
                    Passed = false,
                    Message = $"touched '{touched}' which is not in allowedFiles",
                };
            }
        }

        return new DeterministicVerdict
        {
            JudgeName = "files-within-allowed-set",
            Passed = true,
            Message = touchedFiles.Count == 0
                ? "agent touched no files — vacuously within allowed set"
                : $"all {touchedFiles.Count} touched file(s) are in allowedFiles",
        };
    }

    /// <summary>
    /// Tool-call-count judge. Returns not-applicable when
    /// <paramref name="maxToolCalls"/> is null; otherwise passes iff
    /// <paramref name="observedToolCallCount"/> does not exceed the
    /// declared ceiling.
    /// </summary>
    public static DeterministicVerdict EvaluateToolCallCount(int observedToolCallCount, int? maxToolCalls)
    {
        return maxToolCalls is null
            ? NotApplicable("tool-call-count", "no maxToolCalls declared")
            : new DeterministicVerdict
            {
                JudgeName = "tool-call-count",
                Passed = observedToolCallCount <= maxToolCalls,
                Message = observedToolCallCount <= maxToolCalls
                    ? $"observed {observedToolCallCount} tool call(s) at or under maxToolCalls={maxToolCalls}"
                    : $"observed {observedToolCallCount} tool call(s); maxToolCalls={maxToolCalls}",
            };
    }

    /// <summary>
    /// Cost ceiling judge. Reuses the base
    /// <c>ScenarioAssertions.Cost.MaxUsdMicros</c> field — this WS10
    /// extension does not add a second cost-ceiling field to
    /// <c>EvalExtension</c>. Returns not-applicable when
    /// <paramref name="maxUsdMicros"/> is null; otherwise passes iff
    /// <paramref name="observedCostUsd"/> * 1,000,000 (rounded) is at
    /// or under the declared ceiling.
    /// </summary>
    public static DeterministicVerdict EvaluateCostFromUsage(decimal observedCostUsd, long? maxUsdMicros)
    {
        if (maxUsdMicros is null)
        {
            return NotApplicable("cost-from-usage", "no scenario.assertions.cost.maxUsdMicros declared");
        }

        var observedMicros = (long)decimal.Round(observedCostUsd * 1_000_000m, MidpointRounding.AwayFromZero);

        return new DeterministicVerdict
        {
            JudgeName = "cost-from-usage",
            Passed = observedMicros <= maxUsdMicros,
            Message = observedMicros <= maxUsdMicros
                ? $"observed ${observedCostUsd:0.######} ({observedMicros} micro-USD) at or under ceiling {maxUsdMicros} micro-USD"
                : $"observed ${observedCostUsd:0.######} ({observedMicros} micro-USD); ceiling was {maxUsdMicros} micro-USD",
        };
    }

    /// <summary>
    /// Tests-pass judge. Returns not-applicable when
    /// <paramref name="testCommand"/> is null or empty; otherwise spawns
    /// <c>testCommand[0]</c> with the remaining entries as
    /// <c>ArgumentList</c> (no shell) inside
    /// <paramref name="workingDirectory"/>, bounded by
    /// <paramref name="timeout"/>, with tree-kill on
    /// timeout/cancellation mirroring the tolerant-kill pattern from
    /// <c>RealPiInstallation.RunBunAddAsync</c>. Exit 0 = pass;
    /// non-zero = fail, with the tail of stdout+stderr in the message
    /// (bounded length).
    /// </summary>
    public static async Task<DeterministicVerdict> EvaluateTestsPassAsync(
        IReadOnlyList<string>? testCommand,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (testCommand is null || testCommand.Count == 0)
        {
            return new DeterministicVerdict
            {
                JudgeName = "tests-pass",
                Passed = null,
                Message = "no testCommand declared — not applicable",
            };
        }

        var executable = testCommand[0];
        var arguments = testCommand.Skip(1).ToArray();

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new DeterministicVerdict
                {
                    JudgeName = "tests-pass",
                    Passed = false,
                    Message = $"failed to start '{executable}' — process is null",
                };
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                return new DeterministicVerdict
                {
                    JudgeName = "tests-pass",
                    Passed = false,
                    Message = $"timed out after '{timeout}' or cancelled — killed process tree",
                };
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return new DeterministicVerdict
                {
                    JudgeName = "tests-pass",
                    Passed = true,
                    Message = $"'{executable} {string.Join(' ', arguments)}' exited 0",
                };
            }

            var tail = BuildTail(stdout, stderr, maxChars: 512);
            return new DeterministicVerdict
            {
                JudgeName = "tests-pass",
                Passed = false,
                Message = $"'{executable} {string.Join(' ', arguments)}' exited {process.ExitCode} — tail: {tail}",
            };
        }
        catch (Exception exception)
        {
            return new DeterministicVerdict
            {
                JudgeName = "tests-pass",
                Passed = false,
                Message = $"failed to run '{executable}': {exception.Message}",
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static List<string> EnumerateChangedFiles(string workingDirectory, string pristineFixtureDirectory)
    {
        var changedFiles = new List<string>();

        foreach (var workingFile in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workingDirectory, workingFile);
            var pristineFile = Path.Combine(pristineFixtureDirectory, relativePath);
            if (!File.Exists(pristineFile))
            {
                changedFiles.Add(workingFile);
                continue;
            }

            var workingBytes = File.ReadAllBytes(workingFile);
            var pristineBytes = File.ReadAllBytes(pristineFile);
            if (!workingBytes.AsSpan().SequenceEqual(pristineBytes))
            {
                changedFiles.Add(workingFile);
            }
        }

        return changedFiles;
    }

    /// <summary>Constructs a not-applicable verdict — null passed + a short reason message.</summary>
    private static DeterministicVerdict NotApplicable(string judgeName, string reason)
    {
        return new DeterministicVerdict
        {
            JudgeName = judgeName,
            Passed = null,
            Message = reason + " — not applicable",
        };
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
            // Best-effort teardown — same tolerance as the rest of the
            // real-pi tooling.
        }
    }

    private static string BuildTail(string stdout, string stderr, int maxChars)
    {
        var combined = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n--- stderr ---\n{stderr}";
        return combined.Length <= maxChars
            ? combined
            : "…" + combined[^maxChars..];
    }
}
