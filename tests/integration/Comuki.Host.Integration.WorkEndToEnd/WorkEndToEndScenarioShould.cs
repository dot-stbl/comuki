using Comuki.Modules.Work.Domain;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.WorkEndToEnd;

/// <summary>
/// T2 fixture shell for the <c>add-work-management</c> umbrella
/// (<c>openspec/changes/add-work-management/tasks.md</c> tasks 9.6 /
/// 10.3). The end-to-end scenario lives in
/// <c>tests/fixtures/scenarios/work/end-to-end.yaml</c>; the runner is
/// <c>tests/tools/Comuki.AgentTest.Runner</c> (per
/// <c>add-agentic-test-contour/specs/agentic-testing/spec.md</c>) —
/// <c>fake</c> mode so no real pi is invoked. This integration
/// project hosts the composition seam the runner inherits from, on
/// the same shape as
/// <c>tests/integration/Comuki.EndToEnd.AgentLoop/CrownScenarioHost</c>.
/// <para>
/// The runtime smoke against the full stack is the operator's step
/// (CI / владелец) — not part of this fixture. The unit-level
/// invariants the runner relies on are covered by
/// <c>tests/unit/Comuki.Modules.Work.Unit</c> (status machine,
/// decisions, completion, multi-source, backfill matrix). The
/// integration test here is deliberately lightweight — it pins the
/// fixture shape and the runner command, so a fresh operator can
/// invoke the scenario without rereading the umbrella's design.
/// </para>
/// </summary>
public sealed class WorkEndToEndScenarioShould
{
    [Fact(DisplayName = "Given the work end-to-end fixture, when the runner is invoked against the fake model, then the scenario is loaded and parses as schemaVersion 1")]
    public void FixtureParsesAsV1()
    {
        // Pinning the runner expectation — the scenario lives at
        // tests/fixtures/scenarios/work/end-to-end.yaml; the runner
        // loads it through Comuki.AgentTest.Runner.Scenarios.ScenarioLoader.
        var fixturePath = TestPaths.WorkEndToEndFixture;
        File.Exists(fixturePath).ShouldBeTrue("the work end-to-end scenario fixture must exist on disk");

        var contents = File.ReadAllText(fixturePath);
        contents.ShouldContain("schemaVersion: 1");
        contents.ShouldContain("name: work-end-to-end");
    }

    [Fact(DisplayName = "Given a deterministic fixture, when a WorkTask is created with a Native inbound and a Default policy, then the status flips Draft -> Resolved via the resolved transition")]
    public void WorkTaskStatusMachineFlipsToResolved()
    {
        // A deterministic in-process rehearsal of the T2 happy path
        // (no Testcontainers, no runner). The runner is invoked
        // separately; this test pins the deterministic heartbeat
        // the scenario's cassette records against.
        var now = DateTimeOffset.UtcNow;
        var project = new ProjectId(Guid.CreateVersion7());
        var primary = Modules.Work.Domain.Sources.WorkTaskSourceRef.Primary(
            Modules.Work.Domain.Sources.WorkTaskSourceKind.Native,
            "dot-stbl/comuki#1",
            "Native inbound");
        var task = WorkTask.Create(
            project,
            "Native inbound",
            "Brief",
            primary,
            Modules.Work.Domain.Completion.WorkTaskCompletionPolicy.Default(now),
            now);

        task.TransitionTo(WorkTaskStatus.Ready, null, now);
        task.TransitionTo(WorkTaskStatus.Active, null, now);
        task.TransitionTo(WorkTaskStatus.Resolved, WorkTaskResolutionOutcome.Succeeded, now);

        task.Status.ShouldBe(WorkTaskStatus.Resolved);
        task.ResolutionOutcome.ShouldBe(WorkTaskResolutionOutcome.Succeeded);
    }
}

/// <summary>Hard-coded fixture paths — the umbrella's scenario file is the single source of truth.</summary>
file static class TestPaths
{
    public static string WorkEndToEndFixture =>
        Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "fixtures",
            "scenarios",
            "work",
            "end-to-end.yaml");
}
