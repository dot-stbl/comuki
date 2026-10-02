using Comuki.Modules.Procedures.Application.Materialization;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Materialization;

/// <summary>
/// Unit tests for task 4.3: the late result handler classifies results
/// as authoritative or non-authoritative evidence. A result after a
/// version supersession or generation fence is recorded but does not
/// change the outcome (spec: "changes no outcome, retained as evidence").
/// </summary>
public sealed class LateResultHandlerShould
{
    [Fact(DisplayName = "Given matching version and generation, when handled, then the result is accepted")]
    public void MatchingVersionAndGenerationIsAccepted()
    {
        var clock = TimeProvider.System;

        var record = LateResultHandler.Handle(
            nodeId: "verify",
            pinnedVersionId: "v1",
            currentVersionId: "v1",
            attemptGeneration: "gen-1",
            currentGeneration: "gen-1",
            clock);

        record.Outcome.ShouldBe(LateResultOutcome.Accepted);
        record.NodeId.ShouldBe("verify");
    }

    [Fact(DisplayName = "Given a superseded version, when handled, then the result is non-authoritative evidence")]
    public void SupersededVersionIsNonAuthoritative()
    {
        var clock = TimeProvider.System;

        var record = LateResultHandler.Handle(
            nodeId: "verify",
            pinnedVersionId: "v1",
            currentVersionId: "v2",
            attemptGeneration: "gen-1",
            currentGeneration: "gen-1",
            clock);

        record.Outcome.ShouldBe(LateResultOutcome.NonAuthoritativeEvidence);
    }

    [Fact(DisplayName = "Given a fenced generation, when handled, then the result is non-authoritative evidence")]
    public void FencedGenerationIsNonAuthoritative()
    {
        var clock = TimeProvider.System;

        var record = LateResultHandler.Handle(
            nodeId: "verify",
            pinnedVersionId: "v1",
            currentVersionId: "v1",
            attemptGeneration: "gen-1",
            currentGeneration: "gen-2",
            clock);

        record.Outcome.ShouldBe(LateResultOutcome.NonAuthoritativeEvidence);
    }

    [Fact(DisplayName = "Given both superseded and fenced, when handled, then the result is non-authoritative evidence")]
    public void BothSupersededAndFencedIsNonAuthoritative()
    {
        var clock = TimeProvider.System;

        var record = LateResultHandler.Handle(
            nodeId: "repair",
            pinnedVersionId: "v1",
            currentVersionId: "v3",
            attemptGeneration: "gen-0",
            currentGeneration: "gen-5",
            clock);

        record.Outcome.ShouldBe(LateResultOutcome.NonAuthoritativeEvidence);
        record.PinnedVersionId.ShouldBe("v1");
        record.AttemptGeneration.ShouldBe("gen-0");
    }
}
