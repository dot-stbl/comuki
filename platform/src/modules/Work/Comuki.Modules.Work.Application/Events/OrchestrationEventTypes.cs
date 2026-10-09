namespace Comuki.Modules.Work.Application.Events;

/// <summary>
/// Wire-format type strings the Work-side subscribers consume from
/// the engine's <c>orchestration.outbox_messages</c> table. These
/// are the engine-side producer constants; the Work side owns the
/// matching constant strings as a forward declaration so removing
/// the <c>Comuki.Engine.Orchestration</c> project reference doesn't
/// drag the engine's <c>RunEventTypes</c> enum into the Work
/// module. Each value is part of the wire contract — changing it
/// without coordinating with the engine producer is a wire break.
/// </summary>
public static class OrchestrationEventTypes
{
    /// <summary>Engine-emitted: the Run reached a terminal state (Succeeded / Failed / etc.).</summary>
    public const string RunTerminatedV1 = "orchestration.run.terminated.v1";

    /// <summary>Engine-emitted: the Run was cancelled (the matching <c>orchestration.run.cancelled.v1</c> wire envelope).</summary>
    public const string RunCancelledV1 = "orchestration.run.cancelled.v1";
}
