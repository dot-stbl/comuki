using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// WS7 (issue #87): the outbox payload <see cref="RunProgression.FinalizeAsync"/>
/// stages for a Run's terminal transition — contract name and JSON shape,
/// independent of Postgres. The concurrency / exactly-once-enqueue behavior is
/// covered by RunJournalShould.cs in the Queue integration suite; this class
/// only asserts the message shape.
/// </summary>
public sealed class RunTerminatedOutboxPayloadShould
{
    private static readonly DateTimeOffset occurredAt = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the outbox contract name, when read, then it is orchestration.run.terminated.v1")]
    public void UseTheContractNamedType()
    {
        RunEventTypes.RunTerminatedV1.ShouldBe("orchestration.run.terminated.v1");
    }

    [Fact(DisplayName = "Given a run id, project id and Succeeded status, when the payload is built, then it carries runId/projectId/status/occurredAt")]
    public void CarryRunProjectStatusAndOccurredAt()
    {
        var runId = RunId.New();
        var projectId = ProjectId.New();

        var payload = RunProgression.RunTerminatedPayload(runId, projectId, "Succeeded", occurredAt);

        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("runId").GetGuid().ShouldBe(runId.Value);
        document.RootElement.GetProperty("projectId").GetGuid().ShouldBe(projectId.Value);
        document.RootElement.GetProperty("status").GetString().ShouldBe("Succeeded");
        document.RootElement.GetProperty("occurredAt").GetDateTimeOffset().ShouldBe(occurredAt);
    }

    [Fact(DisplayName = "Given a Failed status, when the payload is built, then status reflects Failed")]
    public void CarryFailedStatus()
    {
        var payload = RunProgression.RunTerminatedPayload(RunId.New(), ProjectId.New(), "Failed", occurredAt);

        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("status").GetString().ShouldBe("Failed");
    }
}
