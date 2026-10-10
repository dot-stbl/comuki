using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Outcomes;
using Comuki.Shared.Contracts.Grpc;

namespace Comuki.Host.Translator.Execution.Loop;

/// <summary>Start/Report envelope builders over a claim and an outcome.</summary>
public static class WorkerEventEnvelope
{
    /// <summary>The first event of a run: which item, which run, what brief.</summary>
    public static WorkerEvent ToStartEvent(ClaimedWorkItemResponse claimed)
    {
        return new WorkerEvent
        {
            Start = new StageStart
            {
                WorkItemId = claimed.WorkItemId.ToString(),
                RunId = claimed.RunId.ToString(),
                Brief = claimed.Brief,
            },
        };
    }

    /// <summary>The last event of a run: the bottom line.</summary>
    public static WorkerEvent ToReportEvent(Guid workItemId, PiOutcome outcome)
    {
        return new WorkerEvent
        {
            Report = new StageReport
            {
                WorkItemId = workItemId.ToString(),
                Status = outcome.Status,
                DurationMs = outcome.DurationMs,
                ResultText = outcome.ResultText,
                ErrorText = outcome.ErrorText,
            },
        };
    }
}
