using Comuki.Engine.Orchestration.Domain.Verification;
using Comuki.Shared.Contracts.Verification;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of <see cref="VerificationRecord"/>:
/// <see cref="VerificationRecord.Create"/> rejects empty gate names and
/// stamps the row in <see cref="GateVerdict.Pending"/>;
/// <see cref="VerificationRecord.FromEvaluation"/> copies the verdict +
/// evidence + evaluator and trims the gate name; the smart-type verdict
/// drives the lower-case wire string used by the verification view.
/// </summary>
public sealed class VerificationRecordShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a non-empty gate name, when Create is called, then the row is Pending with no evidence")]
    public void CreateStampsPending()
    {
        var workItemId = Guid.CreateVersion7();

        var record = VerificationRecord.Create(workItemId, "verify:generic-command-run", now);

        record.WorkItemId.ShouldBe(workItemId);
        record.GateName.ShouldBe("verify:generic-command-run");
        record.Verdict.ShouldBe(GateVerdict.Pending);
        record.EvidenceRefs.ShouldBeEmpty();
        record.EvaluatedAt.ShouldBe(now);
        record.Evaluator.ShouldBe("verify:generic-command-run");
    }

    [Fact(DisplayName = "Given an empty gate name, when Create is called, then it throws")]
    public void RejectEmptyGateNameOnCreate()
    {
        Should.Throw<ArgumentException>(static () => VerificationRecord.Create(
            Guid.CreateVersion7(),
            " ",
            now));
    }

    [Fact(DisplayName = "Given a result with passed verdict and evidence, when FromEvaluation is called, then the row carries them verbatim")]
    public void FromEvaluationCopiesVerdict()
    {
        var workItemId = Guid.CreateVersion7();
        var evidence = new GateEvidenceRef[]
        {
            new(GateEvidenceKind.Stdout, new Uri("comuki://artifacts/abc")),
            new(GateEvidenceKind.Stderr, new Uri("comuki://runs/1/stderr")),
        };
        var result = new GateVerdictResult(GateVerdict.Passed, evidence, "verifier-7");

        var record = VerificationRecord.FromEvaluation(workItemId, "verify:generic-command-run", result, now);

        record.WorkItemId.ShouldBe(workItemId);
        record.GateName.ShouldBe("verify:generic-command-run");
        record.Verdict.ShouldBe(GateVerdict.Passed);
        record.EvidenceRefs.ShouldBe(evidence);
        record.Evaluator.ShouldBe("verifier-7");
        record.EvaluatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a result with a null evaluator, when FromEvaluation is called, then the evaluator falls back to the gate name")]
    public void FromEvaluationFallsBackEvaluator()
    {
        var result = new GateVerdictResult(GateVerdict.Failed, [], " ");

        var record = VerificationRecord.FromEvaluation(Guid.CreateVersion7(), "verify:generic-command-run", result, now);

        record.Evaluator.ShouldBe("verify:generic-command-run");
        record.Verdict.ShouldBe(GateVerdict.Failed);
    }

    [Fact(DisplayName = "Given the verdict, when Value is read, then the lower-case wire string matches the FE mapping")]
    public void GateVerdictWireString()
    {
        GateVerdict.Pending.Value.ShouldBe("pending");
        GateVerdict.Passed.Value.ShouldBe("passed");
        GateVerdict.Failed.Value.ShouldBe("failed");
    }
}
