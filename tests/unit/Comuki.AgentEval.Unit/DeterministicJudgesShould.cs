using Comuki.AgentEval.Judges;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Exercises each of the five deterministic judges against
/// constructed temp-dir fixtures / canned event lists. Only the
/// "tests-pass" facts spawn a real child process; every other fact
/// stays in-process and pure.
/// </summary>
public sealed class DeterministicJudgesShould
{
    [Fact(DisplayName = "Given identical working and pristine directories, when diff-applies runs, then it returns not-pass with a no-files-changed message")]
    public async Task EvaluateDiffAppliesRejectsIdenticalDirectoriesAsync()
    {
        var scratch = NewScratchDir();
        try
        {
            File.WriteAllText(Path.Combine(scratch, "file.txt"), "same content");

            var verdict = await DeterministicJudges.EvaluateDiffAppliesAsync(scratch, scratch, baseDiffAssertion: null, TestContext.Current.CancellationToken);

            verdict.JudgeName.ShouldBe("diff-applies");
            verdict.Passed.ShouldBe(false);
            verdict.Message.ShouldContain("no files");
        }
        finally
        {
            CleanupScratch(scratch);
        }
    }

    [Fact(DisplayName = "Given a working directory with a new file the pristine lacks, when diff-applies runs, then it passes")]
    public async Task EvaluateDiffAppliesPassesOnNewFileAsync()
    {
        var scratch = NewScratchDir();
        var pristine = NewScratchDir();
        try
        {
            File.WriteAllText(Path.Combine(scratch, "new.txt"), "fresh content");
            // pristine stays empty

            var verdict = await DeterministicJudges.EvaluateDiffAppliesAsync(scratch, pristine, baseDiffAssertion: null, TestContext.Current.CancellationToken);

            verdict.Passed.ShouldBe(true);
            verdict.Message.ShouldContain("new.txt");
        }
        finally
        {
            CleanupScratch(scratch);
            CleanupScratch(pristine);
        }
    }

    [Fact(DisplayName = "Given allowedFiles is empty, when files-within-allowed-set runs, then it returns Passed=null with a not-applicable message")]
    public void EvaluateFilesTouchedReturnsNotApplicableWhenAllowedEmpty()
    {
        var verdict = DeterministicJudges.EvaluateFilesTouchedWithinAllowedSet(
            touchedFiles: ["src/x.cs"],
            allowedFiles: []);

        verdict.Passed.ShouldBeNull();
        verdict.Message.ShouldContain("not applicable");
    }

    [Fact(DisplayName = "Given all touched files are in allowedFiles, when files-within-allowed-set runs, then it passes")]
    public void EvaluateFilesTouchedPassesWhenAllAllowed()
    {
        var verdict = DeterministicJudges.EvaluateFilesTouchedWithinAllowedSet(
            touchedFiles: ["src/x.cs", "src/y.cs"],
            allowedFiles: ["src/x.cs", "src/y.cs", "src/z.cs"]);

        verdict.Passed.ShouldBe(true);
    }

    [Fact(DisplayName = "Given a touched file not in allowedFiles, when files-within-allowed-set runs, then it fails and names the disallowed file")]
    public void EvaluateFilesTouchedFailsOnDisallowed()
    {
        var verdict = DeterministicJudges.EvaluateFilesTouchedWithinAllowedSet(
            touchedFiles: ["src/x.cs", "src/forbidden.cs"],
            allowedFiles: ["src/x.cs"]);

        verdict.Passed.ShouldBe(false);
        verdict.Message.ShouldContain("forbidden.cs");
    }

    [Fact(DisplayName = "Given maxToolCalls is null, when tool-call-count runs, then it returns Passed=null")]
    public void EvaluateToolCallCountReturnsNotApplicableWhenMaxNull()
    {
        var verdict = DeterministicJudges.EvaluateToolCallCount(observedToolCallCount: 3, maxToolCalls: null);

        verdict.Passed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given observed is at the ceiling, when tool-call-count runs, then it passes (boundary is inclusive)")]
    public void EvaluateToolCallCountBoundaryInclusive()
    {
        var verdict = DeterministicJudges.EvaluateToolCallCount(observedToolCallCount: 5, maxToolCalls: 5);

        verdict.Passed.ShouldBe(true);
    }

    [Fact(DisplayName = "Given observed exceeds the ceiling, when tool-call-count runs, then it fails")]
    public void EvaluateToolCallCountFailsOverCeiling()
    {
        var verdict = DeterministicJudges.EvaluateToolCallCount(observedToolCallCount: 6, maxToolCalls: 5);

        verdict.Passed.ShouldBe(false);
    }

    [Fact(DisplayName = "Given maxUsdMicros is null, when cost-from-usage runs, then it returns Passed=null")]
    public void EvaluateCostFromUsageReturnsNotApplicableWhenMaxNull()
    {
        var verdict = DeterministicJudges.EvaluateCostFromUsage(observedCostUsd: 1.0m, maxUsdMicros: null);

        verdict.Passed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given observed cost exceeds the micro-USD ceiling, when cost-from-usage runs, then it fails")]
    public void EvaluateCostFromUsageFailsOverCeiling()
    {
        var verdict = DeterministicJudges.EvaluateCostFromUsage(observedCostUsd: 2.5m, maxUsdMicros: 1_000_000L);

        verdict.Passed.ShouldBe(false);
        verdict.Message.ShouldContain("ceiling");
    }

    [Fact(DisplayName = "Given observed cost at exactly the ceiling in micro-USD, when cost-from-usage runs, then it passes")]
    public void EvaluateCostFromUsageBoundaryInclusive()
    {
        var verdict = DeterministicJudges.EvaluateCostFromUsage(observedCostUsd: 1m, maxUsdMicros: 1_000_000L);

        verdict.Passed.ShouldBe(true);
    }

    [Fact(DisplayName = "Given testCommand is null, when tests-pass runs, then it returns Passed=null")]
    public async Task EvaluateTestsPassReturnsNotApplicableWhenCommandNullAsync()
    {
        var verdict = await DeterministicJudges.EvaluateTestsPassAsync(
            testCommand: null,
            workingDirectory: NewScratchDir(),
            timeout: TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        verdict.Passed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a child process that exits zero, when tests-pass runs, then it passes")]
    public async Task EvaluateTestsPassExitsZeroOnSuccessAsync()
    {
        var scratch = NewScratchDir();
        try
        {
            var verdict = await DeterministicJudges.EvaluateTestsPassAsync(
                testCommand: OperatingSystem.IsWindows() ? ["cmd", "/c", "exit 0"] : ["true"],
                workingDirectory: scratch,
                timeout: TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            verdict.Passed.ShouldBe(true);
        }
        finally
        {
            CleanupScratch(scratch);
        }
    }

    [Fact(DisplayName = "Given a child process that exits non-zero, when tests-pass runs, then it fails")]
    public async Task EvaluateTestsPassExitsNonZeroOnFailureAsync()
    {
        var scratch = NewScratchDir();
        try
        {
            var verdict = await DeterministicJudges.EvaluateTestsPassAsync(
                testCommand: OperatingSystem.IsWindows() ? ["cmd", "/c", "exit 1"] : ["false"],
                workingDirectory: scratch,
                timeout: TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            verdict.Passed.ShouldBe(false);
            verdict.Message.ShouldContain("exited 1");
        }
        finally
        {
            CleanupScratch(scratch);
        }
    }

    private static string NewScratchDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-dj-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupScratch(string dir)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
