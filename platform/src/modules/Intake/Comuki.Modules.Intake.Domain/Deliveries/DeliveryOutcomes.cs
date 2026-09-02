namespace Comuki.Modules.Intake.Domain.Deliveries;

/// <summary>
/// Stable outcome labels recorded on every <c>intake_deliveries</c> row —
/// the audit trail of what the webhook pipeline did with each delivery.
/// </summary>
public static class DeliveryOutcomes
{
    /// <summary>A run was launched (watch mode).</summary>
    public const string Admitted = "admitted";

    /// <summary>The ticket is parked in the inbox (inbox mode).</summary>
    public const string Pending = "pending";

    /// <summary>Normalized, but no enabled rule admitted it.</summary>
    public const string Filtered = "filtered";

    /// <summary>Not a ticket event for this source (ping, unrelated event kind) — ignored.</summary>
    public const string Skipped = "skipped";

    /// <summary>An active ticket/run for the issue already exists — one live run per issue.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>Signature verification failed — rejected before any processing.</summary>
    public const string Rejected = "rejected";

    /// <summary>The same delivery id was seen before — replay no-op.</summary>
    public const string Replay = "replay";
}
