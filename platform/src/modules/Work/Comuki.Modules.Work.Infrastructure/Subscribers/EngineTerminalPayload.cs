namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Typed envelope for the engine-side <c>orchestration.run.terminated.v1</c>
/// /
/// <c>orchestration.run.cancelled.v1</c> payloads (the R5 unification —
/// engine is the single producer, Work-side
/// <see cref="WorkIngestRunTerminalSubscriber"/> consumes both
/// shapes). The two payloads share the
/// <c>runId / status</c> pair. Self-contained: the type is a
/// primitive record with the two fields the subscriber reads —
/// no reference to <c>Comuki.Engine.Orchestration</c>, so the
/// Work module stays decoupled from the engine boundary.
/// </summary>
/// <param name="Status">The terminal status string (e.g. <c>Succeeded</c>, <c>Failed</c>, <c>Cancelled</c>); null when absent.</param>
public sealed record EngineTerminalPayload(Guid RunId, string? Status)
{
    /// <summary>Best-effort parse — null when the payload is malformed JSON or has a missing runId.</summary>
    /// <param name="payload">The raw JSON string (the Work subscriber reads it from <c>outbox_messages.payload</c>).</param>
    public static EngineTerminalPayload? TryParse(string payload)
    {
        return WorkTaskEventJson.TryDeserialize<EngineTerminalPayload>(payload);
    }
}
