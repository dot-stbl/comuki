using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;

namespace Comuki.Engine.Orchestration.Infrastructure.Journal;

/// <summary>
/// Journal payload builders shared by the queue implementation and the lease
/// reaper — one shape per event family, camelCase via
/// <see cref="JsonSerializerOptions.Web"/>.
/// </summary>
internal static class WorkItemEventPayloads
{
    /// <summary>Payload for a claim-driven status change (queued -> running).</summary>
    /// <param name="itemId"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="workerId"></param>
    /// <param name="attempt"></param>
    public static string StatusChanged(Guid itemId, string from, string to, Guid workerId, int attempt)
    {
        return JsonSerializer.Serialize(new { itemId, from, to, workerId, attempt }, JsonSerializerOptions.Web);
    }

    /// <summary>Payload for a worker-driven terminal transition, embedding the result/reason detail.</summary>
    /// <param name="itemId"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="detail"></param>
    public static string StatusChangedWithDetail(Guid itemId, string from, string to, object detail)
    {
        return JsonSerializer.Serialize(new { itemId, from, to, detail }, JsonSerializerOptions.Web);
    }

    /// <summary>Payload for a reaped lease (running -> queued requeue or running -> failed).</summary>
    /// <param name="itemId"></param>
    /// <param name="to"></param>
    /// <param name="attempt"></param>
    public static string LeaseExpired(Guid itemId, string to, int attempt)
    {
        return JsonSerializer.Serialize(new { itemId, from = nameof(WorkItemStatus.Running), to, attempt }, JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Payload for <c>worker.admitted</c> (add-worker-admission task 1.2,
    /// spec §"Journal admission"): the admission id, env class, profile key,
    /// and isolation class. Secret refs are deliberately omitted — the
    /// journal must never carry secret values, and the Translator is the
    /// only consumer that needs the refs.
    /// </summary>
    /// <param name="admissionId">Stable admission id (UUIDv7); the slot's identity.</param>
    /// <param name="envClass">Catalog id the slot binds to (toolchain axis).</param>
    /// <param name="profileKey">Control-plane profile key (role axis).</param>
    /// <param name="isolationClass">Isolation strength the slot requires (<c>trusted-process</c> | <c>strong</c>).</param>
    public static string WorkerAdmitted(Guid admissionId, string envClass, string profileKey, string isolationClass)
    {
        return JsonSerializer.Serialize(new { admissionId, envClass, profileKey, isolationClass }, JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Payload for <c>worker.admission_denied</c>: the typed
    /// the <c>SlotAdmission.Codes</c> class in Engine.Compute.Admission
    /// denial code (e.g. <c>admission.publisher</c>) and the admission id
    /// when the slot had one minted before the deny.
    /// </summary>
    /// <param name="denialCode">Stable typed code from <c>SlotAdmission.Codes</c>.</param>
    /// <param name="admissionId">Slot's admission id when minted; null on a pre-evaluation deny.</param>
    public static string WorkerAdmissionDenied(string denialCode, Guid? admissionId)
    {
        return JsonSerializer.Serialize(new { denialCode, admissionId }, JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Payload for <c>gate.evaluated</c> (add-orchestra §3 — Coda,
    /// <c>verification/spec.md</c> Requirement "gate_evaluated journal
    /// event"). Carries the gate name, the verdict, the evidence URIs
    /// and the work item + record ids so the timeline reader can join
    /// the <c>verifications</c> row directly. The record id is the
    /// one the host stamped at upsert — readers use it to disambiguate
    /// repeated re-evaluations of the same (work item, gate) pair
    /// (a re-evaluation is an updated row, not a new one, and the
    /// journal keeps the full trail).
    /// </summary>
    /// <param name="workItemId">Work item the gate evaluated.</param>
    /// <param name="recordId"><c>VerificationRecord.Id</c> stamped at upsert.</param>
    /// <param name="gateName">Stable gate name (matches <c>IVerificationGateProvider.GateName</c>).</param>
    /// <param name="verdict">The gate verdict — <c>pending</c> / <c>passed</c> / <c>failed</c>.</param>
    /// <param name="evidenceRefs">Canonical evidence URIs the gate attached (may be empty).</param>
    /// <param name="evaluator">Provider name stamped on the row.</param>
    public static string GateEvaluated(
        Guid workItemId,
        Guid recordId,
        string gateName,
        string verdict,
        IReadOnlyList<string> evidenceRefs,
        string evaluator)
    {
        return JsonSerializer.Serialize(
            new
            {
                workItemId,
                recordId,
                gateName,
                verdict,
                evidenceRefs,
                evaluator,
            },
            JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Payload for <c>merge_queue.run_referenced</c> (add-orchestra
    /// §3 — Coda, task 3.6 restore the run link). Carries the
    /// merge-queue row id and the run id it now references; the
    /// <c>kind</c> discriminator lets a reader distinguish
    /// <c>MergeQueueEntry</c> from <c>MergeBatch</c> rows on the
    /// timeline without inspecting the schema.
    /// </summary>
    /// <param name="kind">Discriminator — <c>"entry"</c> or <c>"batch"</c>.</param>
    /// <param name="rowId">The <c>MergeQueueEntry.Id</c> or <c>MergeBatch.Id</c> the platform stamped the run id on.</param>
    /// <param name="runId">The run id the row now references.</param>
    public static string MergeQueueRunReferenced(string kind, Guid rowId, Guid runId)
    {
        return JsonSerializer.Serialize(
            new { kind, rowId, runId },
            JsonSerializerOptions.Web);
    }
}
