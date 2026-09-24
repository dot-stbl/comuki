using Comuki.AgentTest.Runner.Journal;
using Comuki.AgentTest.Runner.Scenarios;
using Shouldly;
using Xunit;

namespace Comuki.AgentTest.Runner.Unit;

/// <summary>
/// Proves the loader against the two real, committed scenario fixtures
/// this workstream ships (<c>tests/fixtures/scenarios/small-repo/</c>) —
/// no Podman/container dependency, fast and deterministic. Also covers the
/// structural validation rules the spec calls out explicitly.
/// </summary>
public sealed class ScenarioLoaderShould
{
    [Fact(DisplayName = "Given the add-null-check fixture, when loaded, then every field round-trips including the YAML list properties")]
    public void LoadAddNullCheckFixtureAsync()
    {
        var scenario = ScenarioLoader.Load(FixturePath("add-null-check.scenario.yaml"));

        scenario.SchemaVersion.ShouldBe(1);
        scenario.Name.ShouldBe("add-null-check");
        scenario.Ticket.Labels.ShouldBe(["bug", "comuki"]);
        scenario.Ticket.TargetRepo.Fixture.ShouldBe("small-repo");
        scenario.Worker.Image.ShouldBe("comuki-agent-test-worker:ws6");
        scenario.Worker.ProfileKey.ShouldBe("implement");
        scenario.Model.ShouldNotBeNull();
        scenario.Model!.Mode.ShouldBe(ScenarioModelMode.Fake);
        scenario.ExpectedTrajectory.ShouldHaveSingleItem();
        scenario.ExpectedTrajectory[0].ToolsUsed.ShouldBe(["Bash"]);
        scenario.ExpectedTrajectory[0].ForbiddenTools.ShouldBe(["WebFetch"]);
        scenario.Assertions.Journal.Select(static condition => condition.Condition)
            .ShouldBe([JournalConditionEvaluator.WorkspacePrepared, JournalConditionEvaluator.AgentRunning]);
        scenario.Assertions.Run.ShouldNotBeNull();
        scenario.Assertions.Run!.FinalStatus.ShouldBe("succeeded");
    }

    [Fact(DisplayName = "Given the bad-image-label fixture, when loaded, then worker.image is the deliberately nonexistent tag")]
    public void LoadBadImageLabelFixtureAsync()
    {
        var scenario = ScenarioLoader.Load(FixturePath("bad-image-label.scenario.yaml"));

        scenario.Name.ShouldBe("bad-image-label");
        scenario.Worker.Image.ShouldBe("comuki-agent-test-worker:ws6-nonexistent-tag");
    }

    [Fact(DisplayName = "Given a missing scenario file, when loaded, then it fails with a message naming the path, not a stack trace")]
    public void FailCleanlyWhenFileMissing()
    {
        var missingPath = Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", "does-not-exist.scenario.yaml");

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(missingPath));

        exception.Message.ShouldContain("does-not-exist.scenario.yaml");
    }

    [Fact(DisplayName = "Given a scenario declaring an unsupported schemaVersion, when loaded, then it fails naming the version")]
    public void RejectUnsupportedSchemaVersion()
    {
        var path = WriteTempScenario(
            /*lang=yaml*/
            """
            schemaVersion: 2
            name: future-schema
            ticket:
              title: t
            worker:
              image: img:tag
            """);

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(path));

        exception.Message.ShouldContain("schemaVersion 2");
    }

    [Fact(DisplayName = "Given a scenario with no worker.image, when loaded, then it fails naming the missing field")]
    public void RejectMissingWorkerImage()
    {
        var path = WriteTempScenario(
            /*lang=yaml*/
            """
            schemaVersion: 1
            name: no-image
            ticket:
              title: t
            """);

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(path));

        exception.Message.ShouldContain("no-image");
        exception.Message.ShouldContain("worker.image");
    }

    [Fact(DisplayName = "Given model.mode: replay with no cassette field, when loaded, then it fails before any run is attempted")]
    public void RejectReplayModeWithNoCassette()
    {
        var path = WriteTempScenario(
            /*lang=yaml*/
            """
            schemaVersion: 1
            name: replay-no-cassette
            ticket:
              title: t
            worker:
              image: img:tag
            model:
              mode: replay
            """);

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(path));

        exception.Message.ShouldContain("replay");
        exception.Message.ShouldContain("cassette");
    }

    [Fact(DisplayName = "Given model.mode: replay pointing at a cassette that does not exist on disk, when loaded, then it names the missing cassette")]
    public void RejectReplayModeWithMissingCassetteFile()
    {
        var path = WriteTempScenario(
            /*lang=yaml*/
            """
            schemaVersion: 1
            name: replay-missing-file
            ticket:
              title: t
            worker:
              image: img:tag
            model:
              mode: replay
              cassette: cassettes/does-not-exist.json
            """);

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(path));

        exception.Message.ShouldContain("does-not-exist.json");
    }

    [Theory(DisplayName = "Given a model.mode value in any case, when loaded, then it maps to the right enum member")]
    [InlineData("fake", ScenarioModelMode.Fake)]
    [InlineData("FAKE", ScenarioModelMode.Fake)]
    [InlineData("live", ScenarioModelMode.Live)]
    [InlineData("LIVE", ScenarioModelMode.Live)]
    public void ParseModelModeCaseInsensitively(string yamlValue, ScenarioModelMode expected)
    {
        var path = WriteTempScenario(
            $"""
            schemaVersion: 1
            name: mode-case-{yamlValue.ToLowerInvariant()}
            ticket:
              title: t
            worker:
              image: img:tag
            model:
              mode: {yamlValue}
            """);

        var scenario = ScenarioLoader.Load(path);

        scenario.Model.ShouldNotBeNull();
        scenario.Model!.Mode.ShouldBe(expected);
    }

    [Fact(DisplayName = "Given an unrecognized model.mode value, when loaded, then it fails naming the bad value and the known set")]
    public void RejectUnknownModelMode()
    {
        var path = WriteTempScenario(
            /*lang=yaml*/
            """
            schemaVersion: 1
            name: bad-mode
            ticket:
              title: t
            worker:
              image: img:tag
            model:
              mode: sandbox
            """);

        var exception = Should.Throw<ScenarioValidationException>(() => ScenarioLoader.Load(path));

        exception.Message.ShouldContain("sandbox");
        exception.Message.ShouldContain("fake|replay|live");
    }

    private static string WriteTempScenario(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comuki-agenttest-unit-{Guid.NewGuid():N}.scenario.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "tests", "fixtures", "scenarios", "small-repo", fileName);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("could not locate the repo root (comuki.slnx) from " + AppContext.BaseDirectory);
    }
}
