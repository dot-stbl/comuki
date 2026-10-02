using Comuki.Modules.Procedures.Application.Runtime;
using Comuki.Modules.Procedures.Application.Runtime.Coordination;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Runtime;

/// <summary>
/// Unit tests for task 5.1: the repair boundary engine opens numbered
/// generations up to the cap, refuses on exhaustion, and classifies
/// outcome ports so inconclusive and infrastructure-error never open a
/// semantic generation (spec: bounded repair boundaries).
/// </summary>
public sealed class RepairBoundaryEngineShould
{
    [Fact(DisplayName = "Given generation 0 of 2, when OpenGeneration runs, then it returns generation 1 with the failed node named")]
    public void OpensFirstGeneration()
    {
        var engine = new RepairBoundaryEngine(TimeProvider.System);

        var generation = engine.OpenGeneration(0, 2, "verify", "test-report-abc");

        generation.GenerationNumber.ShouldBe(1);
        generation.FailedNodeId.ShouldBe("verify");
        generation.FailingEvidenceRef.ShouldBe("test-report-abc");
        generation.OpenedAt.ShouldNotBe(default);
    }

    [Fact(DisplayName = "Given generation 2 of 2 (at cap), when OpenGeneration runs, then it throws GenerationsExhausted")]
    public void ExhaustedThrows()
    {
        var engine = new RepairBoundaryEngine(TimeProvider.System);

        var exception = Should.Throw<ProcedureRuntimeException>(
            () => engine.OpenGeneration(2, 2, "verify", "test-report-abc"));

        exception.Code.ShouldBe(ProcedureRuntimeException.GenerationsExhausted);
        exception.Message.ShouldContain("verify");
        exception.Message.ShouldContain("2");
    }

    [Fact(DisplayName = "Given a failed outcome, when classified, then it opens a new generation")]
    public void FailedOpensNewGeneration()
    {
        RepairBoundaryEngine.ClassifyOutcome("failed").ShouldBe(RepairAction.OpenNewGeneration);
    }

    [Fact(DisplayName = "Given an inconclusive outcome, when classified, then it escalates (never loops)")]
    public void InconclusiveEscalates()
    {
        RepairBoundaryEngine.ClassifyOutcome("inconclusive").ShouldBe(RepairAction.Escalate);
    }

    [Fact(DisplayName = "Given an infrastructure-error outcome, when classified, then it retries execution (no semantic generation)")]
    public void InfrastructureErrorRetriesExecution()
    {
        RepairBoundaryEngine.ClassifyOutcome("infrastructure-error").ShouldBe(RepairAction.RetryExecution);
    }

    [Fact(DisplayName = "Given a passed outcome, when classified, then it continues")]
    public void PassedContinues()
    {
        RepairBoundaryEngine.ClassifyOutcome("passed").ShouldBe(RepairAction.Continue);
    }
}
