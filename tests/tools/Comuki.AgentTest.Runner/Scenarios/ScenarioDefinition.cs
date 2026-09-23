namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// One declarative scenario (design.md "Scenario format" of
/// add-agentic-test-contour): a ticket fixture, the worker/model wiring,
/// an expected tool-call trajectory and the assertions a
/// <see cref="ScenarioModelMode"/> run must satisfy. Deserialized from YAML
/// by <see cref="ScenarioLoader"/> — property names here are
/// camelCase-mapped onto the YAML keys, not renamed per-field.
/// </summary>
public sealed record ScenarioDefinition
{
    /// <summary>Schema version of this file's shape; the loader rejects anything but <c>1</c>.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Short, unique, kebab-case scenario name — also its report key and container-name suffix.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Free-text description of what the scenario exercises and why.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The inbound ticket this scenario seeds.</summary>
    public ScenarioTicket Ticket { get; init; } = new();

    /// <summary>Claim labels and (T2a-only) the pi fixture stream the worker container replays.</summary>
    public ScenarioWorker Worker { get; init; } = new();

    /// <summary>
    /// Model wiring — reserved for T2b/T4 (WS7/WS9): a real <c>pi</c> pointed
    /// at the fake-model/replay/live server. Null (or a bare <c>mode: fake</c>
    /// with no cassette) is normal for a T2a scenario, which never calls a
    /// model at all — <see cref="ScenarioWorker.PiFixturesDir"/> is what
    /// drives <c>TestFakePi</c> instead.
    /// </summary>
    public ScenarioModel? Model { get; init; }

    /// <summary>Expected tool-call trajectory per stage — asserted against the tools pi/TestFakePi actually invoked.</summary>
    public List<ExpectedTrajectoryStage> ExpectedTrajectory { get; init; } = [];

    /// <summary>The assertions this scenario's run must satisfy.</summary>
    public ScenarioAssertions Assertions { get; init; } = new();

    /// <summary>Live/replay-record budget ceiling — reserved for T4/WS8/WS9; ignored in T2a.</summary>
    public ScenarioBudget? Budget { get; init; }
}

/// <summary>The inbound ticket a scenario seeds through the real webhook endpoint.</summary>
public sealed record ScenarioTicket
{
    /// <summary>Intake source key (matches an intake webhook fixture shape) — <c>github</c> is the only wired provider today.</summary>
    public string Source { get; init; } = "github";

    /// <summary>Ticket title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Ticket body (markdown).</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Labels the admission rule filters on.</summary>
    public List<string> Labels { get; init; } = [];

    /// <summary>The fixture target repo the worker's workspace is prepared from.</summary>
    public ScenarioTargetRepo TargetRepo { get; init; } = new();
}

/// <summary>
/// A fixture target repo under <c>tests/fixtures/target-repos/</c>, cloned
/// through the <c>SourceGitUrl</c>/<c>SourceGitRef</c> mechanism per D4 —
/// see <c>Comuki.EndToEnd.AgentLoop</c>'s README note: that clone-into-
/// container seam is not landed yet (harden-pi-worker-sandbox task 4.1/4.3),
/// so a T2a run does not depend on this being mounted into the container.
/// </summary>
public sealed record ScenarioTargetRepo
{
    /// <summary>Directory name under <c>tests/fixtures/target-repos/</c>.</summary>
    public string Fixture { get; init; } = string.Empty;

    /// <summary>Git ref to check out.</summary>
    public string Ref { get; init; } = "main";
}

/// <summary>
/// Claim labels the runner stamps on <c>ComputeStartRequest</c> (and the
/// matching queued <c>WorkItem</c> row) plus the T2a-only TestFakePi
/// fixture selection.
/// </summary>
public sealed record ScenarioWorker
{
    /// <summary>Worker image tag the container is provisioned from (claim label — must equal <c>COMUKI_WORKER_IMAGE</c>).</summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>Profile key claim label.</summary>
    public string ProfileKey { get; init; } = "implement";

    /// <summary>Pinned profiles-git-ref claim label.</summary>
    public string ProfilesRef { get; init; } = "test";

    /// <summary>
    /// Relative path (under the scenario file's directory) to a directory of
    /// pi-native stream-json fixture files TestFakePi should stream. T2a
    /// only — TestFakePi has no seam to receive <c>--fixtures-dir</c> from a
    /// real claim (<c>Comuki.Host.Translator.Runtime.PiRunner</c> always
    /// spawns it bare), so this is honored by baking the directory into the
    /// test worker image at build time, not by an argument. Empty =
    /// TestFakePi's own bundled default fixtures.
    /// </summary>
    public string? PiFixturesDir { get; init; }

    /// <summary>Forced TestFakePi exit code — 0 (success) unless set; a scenario proving a failure path sets this.</summary>
    public int? PiExitCode { get; init; }
}

/// <summary>Model wiring — reserved for T2b (WS7) and beyond; the T2a runner reads only <see cref="Mode"/>, and only for reporting.</summary>
public sealed record ScenarioModel
{
    /// <summary>fake | replay | live.</summary>
    public ScenarioModelMode Mode { get; init; } = ScenarioModelMode.Fake;

    /// <summary>Cassette path (relative to the scenario file) — required when <see cref="Mode"/> is <c>replay</c>.</summary>
    public string? Cassette { get; init; }

    /// <summary>Fake-model script path (relative to the scenario file) — WS7+; unused by T2a.</summary>
    public string? FakeScript { get; init; }
}

/// <summary>One expected trajectory stage — asserted against the tools pi/TestFakePi actually invoked in that stage.</summary>
public sealed record ExpectedTrajectoryStage
{
    /// <summary>Stage name (e.g. <c>plan</c>, <c>work-item</c>).</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Minimum tool calls expected in this stage; null = unchecked.</summary>
    public int? MinToolCalls { get; init; }

    /// <summary>Maximum tool calls expected in this stage; null = unchecked.</summary>
    public int? MaxToolCalls { get; init; }

    /// <summary>Tool names that must appear at least once in this stage; empty = unchecked.</summary>
    public List<string> ToolsUsed { get; init; } = [];

    /// <summary>Tool names that must never appear in this stage (worker-sdk lock/allowlist proof — T2b/WS7).</summary>
    public List<string> ForbiddenTools { get; init; } = [];
}

/// <summary>The assertion blocks a scenario run is checked against.</summary>
public sealed record ScenarioAssertions
{
    /// <summary>Journal-condition assertions (see <see cref="Journal.JournalConditionEvaluator"/> for the known vocabulary).</summary>
    public List<JournalConditionAssertion> Journal { get; init; } = [];

    /// <summary>Final work-item/run status assertion.</summary>
    public RunAssertion? Run { get; init; }

    /// <summary>Diff assertions against the fixture repo — T2b/WS7+ (T2a runs TestFakePi, which never edits the workspace).</summary>
    public DiffAssertion? Diff { get; init; }

    /// <summary>Cost ceiling assertions — replay/live only (WS8/WS9); ignored in fake/T2a.</summary>
    public CostAssertion? Cost { get; init; }

    /// <summary>LLM-as-judge rubric assertion — T4/WS10 only; ignored everywhere else.</summary>
    public JudgeAssertion? Judge { get; init; }
}

/// <summary>One journal-condition assertion: a named condition must evaluate to <see cref="Expected"/>.</summary>
public sealed record JournalConditionAssertion
{
    /// <summary>Condition name — see <see cref="Journal.JournalConditionEvaluator"/> for the vocabulary this runner understands.</summary>
    public string Condition { get; init; } = string.Empty;

    /// <summary>Expected truth value.</summary>
    public bool Expected { get; init; } = true;
}

/// <summary>Final-status assertion against the completed work item / run.</summary>
public sealed record RunAssertion
{
    /// <summary>Expected final work-item status: <c>succeeded</c> or <c>failed</c> (matches <c>WorkItemStatus</c>, case-insensitive).</summary>
    public string FinalStatus { get; init; } = "succeeded";
}

/// <summary>Diff assertions against the fixture repo's working tree after the run — reserved for T2b/WS7.</summary>
public sealed record DiffAssertion
{
    /// <summary>Files the diff must have touched, each with an optional required substring.</summary>
    public List<DiffFileAssertion> FilesChanged { get; init; } = [];

    /// <summary>Whether the diff must include a new/changed test file.</summary>
    public bool? TestsAdded { get; init; }
}

/// <summary>One expected changed file in a diff assertion.</summary>
public sealed record DiffFileAssertion
{
    /// <summary>Path relative to the fixture repo root.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Substring the file's new content must contain; null = unchecked.</summary>
    public string? MustContain { get; init; }
}

/// <summary>Cost-ceiling assertion — replay/live only (WS8/WS9).</summary>
public sealed record CostAssertion
{
    /// <summary>Maximum micro-USD the run may have spent.</summary>
    public long? MaxUsdMicros { get; init; }
}

/// <summary>LLM-as-judge rubric assertion — T4/WS10 only.</summary>
public sealed record JudgeAssertion
{
    /// <summary>Rubric file path (relative to the scenario file).</summary>
    public string Rubric { get; init; } = string.Empty;

    /// <summary>Minimum passing score, 0..1.</summary>
    public double MinScore { get; init; }
}

/// <summary>Live-mode budget ceiling — WS9 only.</summary>
public sealed record ScenarioBudget
{
    /// <summary>Maximum USD the runner lets a live run spend before aborting mid-run.</summary>
    public decimal MaxUsd { get; init; }
}
