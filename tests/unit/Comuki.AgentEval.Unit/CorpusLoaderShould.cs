using Comuki.AgentEval.Corpus;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Exercises <see cref="CorpusLoader"/> against temp-dir fixtures covering
/// the eval-block shapes the WS10 corpus declares: full eval block with
/// rubric, eval block entirely absent (defaults kick in), rubric
/// weights that don't sum to 1 (still loads fine), and exact-name
/// filter via <c>filterName</c>.
/// </summary>
public sealed class CorpusLoaderShould
{
    [Fact(DisplayName = "Given a corpus with no scenarios, when loaded, then the result is empty")]
    public void ReturnEmptyListForEmptyDirectory()
    {
        var corpusDir = CreateTempCorpus();

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a missing corpus directory, when loaded, then it fails with a path-naming message")]
    public void FailCleanlyWhenDirectoryMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-corpus-{Guid.NewGuid():N}");

        var exception = Should.Throw<CorpusValidationException>(() => CorpusLoader.LoadDirectory(missing));

        exception.Message.ShouldContain(missing);
    }

    [Fact(DisplayName = "Given a scenario with a full eval block (rubric + weights), when loaded, then the rubric and every criterion round-trip")]
    public void LoadFullEvalBlock()
    {
        var corpusDir = CreateTempCorpus();
        var path = WriteScenario(corpusDir, "with-rubric", withEval:
            "eval:\n  difficulty: hard\n  expectedOutcome: A clean refactor that the rubric scores highly.\n  allowedFiles:\n    - \"src/OrderTotal.cs\"\n  maxToolCalls: 5\n  rubric:\n    criteria:\n      - id: correctness\n        description: Correctness of the fix.\n        weight: 2\n      - id: style\n        description: Code style.\n        weight: 1\n    minScore: 0.6\n");

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.Count.ShouldBe(1);
        var entry = entries[0];
        entry.Scenario.SourcePath.ShouldBe(Path.GetFullPath(path));
        entry.Eval.Difficulty.ShouldBe("hard");
        entry.Eval.ExpectedOutcome.ShouldContain("clean refactor");
        entry.Eval.AllowedFiles!.Count.ShouldBe(1);
        entry.Eval.AllowedFiles![0].ShouldBe("src/OrderTotal.cs");
        entry.Eval.MaxToolCalls.ShouldBe(5);
        entry.Eval.Rubric.ShouldNotBeNull();
        entry.Eval.Rubric!.Criteria.Count.ShouldBe(2);
        entry.Eval.Rubric!.Criteria[0].Id.ShouldBe("correctness");
        entry.Eval.Rubric!.Criteria[0].Weight.ShouldBe(2d);
        entry.Eval.Rubric!.Criteria[1].Id.ShouldBe("style");
        entry.Eval.Rubric!.MinScore.ShouldBe(0.6);
    }

    [Fact(DisplayName = "Given a scenario with no eval block, when loaded, then every eval field defaults to its empty value")]
    public void DefaultEvalBlockWhenAbsent()
    {
        var corpusDir = CreateTempCorpus();
        WriteScenario(corpusDir, "no-eval", withEval: "");

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.Count.ShouldBe(1);
        var entry = entries[0];
        entry.Eval.Difficulty.ShouldBe(string.Empty);
        entry.Eval.ExpectedOutcome.ShouldBe(string.Empty);
        entry.Eval.AllowedFiles.ShouldBeNull();
        entry.Eval.MaxToolCalls.ShouldBeNull();
        entry.Eval.Rubric.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a scenario with a rubric whose weights do not sum to 1, when loaded, then the loader still accepts it (weights are normalized downstream)")]
    public void RubricWeightsNotRequiredToSumToOne()
    {
        var corpusDir = CreateTempCorpus();
        WriteScenario(corpusDir, "weights-dont-sum",
            withEval:
                "eval:\n  rubric:\n    criteria:\n      - id: only\n        description: The only criterion.\n        weight: 7\n    minScore: 0.5\n");

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.Count.ShouldBe(1);
        entries[0].Eval.Rubric.ShouldNotBeNull();
        entries[0].Eval.Rubric!.Criteria[0].Weight.ShouldBe(7d);
    }

    [Fact(DisplayName = "Given multiple scenarios and a filterName, when loaded, then only the matching entry is returned")]
    public void FilterByName()
    {
        var corpusDir = CreateTempCorpus();
        WriteScenario(corpusDir, "alpha", withEval: "");
        WriteScenario(corpusDir, "beta", withEval: "");

        var entries = CorpusLoader.LoadDirectory(corpusDir, filterName: "beta");

        entries.Count.ShouldBe(1);
        entries[0].Scenario.Name.ShouldBe("beta");
    }

    [Fact(DisplayName = "Given an anonymized-real subfolder, when loading, then no scenario files in it are picked up (folder is inert by design)")]
    public void SkipAnonymizedRealSubfolder()
    {
        var corpusDir = CreateTempCorpus();
        var anonymizedRealDir = Path.Combine(corpusDir, "anonymized-real");
        Directory.CreateDirectory(anonymizedRealDir);
        WriteScenario(anonymizedRealDir, "should-not-load", withEval: "");
        WriteScenario(corpusDir, "should-load", withEval: "");

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.Count.ShouldBe(1);
        entries[0].Scenario.Name.ShouldBe("should-load");
    }

    [Fact(DisplayName = "Given scenario files inside scripts/ and cassettes/ subfolders, when loading, then those are skipped")]
    public void SkipScriptsAndCassettesSubfolders()
    {
        var corpusDir = CreateTempCorpus();
        var scriptsDir = Path.Combine(corpusDir, "scripts");
        var cassettesDir = Path.Combine(corpusDir, "cassettes");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(cassettesDir);
        WriteScenario(scriptsDir, "should-not-load-scripts", withEval: "");
        WriteScenario(cassettesDir, "should-not-load-cassettes", withEval: "");
        WriteScenario(corpusDir, "should-load", withEval: "");

        var entries = CorpusLoader.LoadDirectory(corpusDir);

        entries.Count.ShouldBe(1);
        entries[0].Scenario.Name.ShouldBe("should-load");
    }

    private static string CreateTempCorpus()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"comuki-agent-eval-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteScenario(string directory, string scenarioName, string withEval)
    {
        var path = Path.Combine(directory, $"{scenarioName}.scenario.yaml");
        var baseYaml = string.IsNullOrEmpty(withEval)
            ? $"schemaVersion: 1\nname: {scenarioName}\ndescription: unit-test scenario\nticket:\n  title: t\n  body: b\nworker:\n  image: comuki-agent-test-worker:ws6\n"
            : $"schemaVersion: 1\nname: {scenarioName}\ndescription: unit-test scenario\nticket:\n  title: t\n  body: b\nworker:\n  image: comuki-agent-test-worker:ws6\n{withEval}";
        File.WriteAllText(path, baseYaml);
        return path;
    }
}
