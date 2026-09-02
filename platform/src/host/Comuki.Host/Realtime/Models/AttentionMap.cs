using System.Text.Json;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.Host.Realtime.Models;

/// <summary>
/// Journal entry → attention signal: the truth table of which transitions
/// are worth pushing to the project attention group. Work-item events parse
/// their <c>to</c> status; run transitions are mapped for forward
/// compatibility (nothing emits <c>run.status_changed</c> yet).
/// </summary>
internal static class AttentionMap
{
    /// <summary>A work item or run started executing.</summary>
    public const string KindRunning = "running";

    /// <summary>A work item or run failed.</summary>
    public const string KindFailed = "failed";

    /// <summary>A run was escalated to a human.</summary>
    public const string KindEscalated = "escalated";

    /// <summary>A run paused on a human decision (plan approval).</summary>
    public const string KindAwaitingApproval = "awaiting_approval";

    /// <summary>The platform-owned journal types attention is derived from.</summary>
    public const string RunStatusChanged = "run.status_changed";

    /// <summary>Maps one entry to its attention draft; null when the transition is not attention-worthy.</summary>
    /// <param name="entry"></param>
    /// <returns></returns>
    public static AttentionDraft? FromEntry(RunEventEntry entry)
    {
        return entry.Type switch
        {
            "work_item.status_changed" => FromWorkItemStatus(entry),
            "work_item.lease_expired" => FromWorkItemStatus(entry),
            RunStatusChanged => FromRunStatus(entry),
            _ => null,
        };
    }

    /// <summary>
    /// Work-item transitions: <c>Running</c> → running, <c>Failed</c> →
    /// failed. Everything else — succeeded, cancelled, and the lease-expiry
    /// requeue to <c>Queued</c> — is deliberately not attention-worthy: the
    /// requeued item goes back to the queue and its next claim signals
    /// again.
    /// </summary>
    private static AttentionDraft? FromWorkItemStatus(RunEventEntry entry)
    {
        var to = ReadTo(entry);

        return to switch
        {
            "Running" => new AttentionDraft(to, KindRunning, RunEventViewMapping.ReadWorkItemId(entry)),
            "Failed" => new AttentionDraft(to, KindFailed, RunEventViewMapping.ReadWorkItemId(entry)),
            _ => null,
        };
    }

    /// <summary>Run transitions: running / failed / escalated / waiting-on-approval.</summary>
    private static AttentionDraft? FromRunStatus(RunEventEntry entry)
    {
        var to = ReadTo(entry);

        return to switch
        {
            "Running" => new AttentionDraft(to, KindRunning, null),
            "Failed" => new AttentionDraft(to, KindFailed, null),
            "Escalated" => new AttentionDraft(to, KindEscalated, null),
            "Waiting" => new AttentionDraft(to, KindAwaitingApproval, null),
            _ => null,
        };
    }

    /// <summary>Reads the <c>to</c> property of a transition payload; null when absent or unparsable.</summary>
    private static string? ReadTo(RunEventEntry entry)
    {
        try
        {
            using var document = JsonDocument.Parse(entry.PayloadJson);

            return document.RootElement.TryGetProperty("to", out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>An attention-worthy transition before its project is resolved.</summary>
/// <param name="Status">Target status (PascalCase domain name).</param>
/// <param name="AttentionKind">Lowercase wire kind.</param>
/// <param name="WorkItemId">The transitioning work item, when applicable.</param>
internal sealed record AttentionDraft(string Status, string AttentionKind, Guid? WorkItemId);
