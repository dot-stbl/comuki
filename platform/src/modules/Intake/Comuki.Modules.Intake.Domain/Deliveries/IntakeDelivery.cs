namespace Comuki.Modules.Intake.Domain.Deliveries;

/// <summary>
/// One received webhook delivery — the insert-first idempotency lock:
/// the unique index on <c>(source, delivery_id)</c> guarantees the same
/// letter is never processed twice (a conflicting insert is a replay →
/// 200 no-op). The delivery id is the provider's id header when it has
/// one (GitHub <c>X-GitHub-Delivery</c>, GitLab
/// <c>X-Gitlab-Event-UUID</c>) and a SHA-256 of the raw body otherwise.
/// </summary>
public sealed class IntakeDelivery
{
    internal IntakeDelivery()
    {
    }

    /// <summary>Delivery id (UUIDv7, client-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Kebab-case source key the delivery arrived on.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>Provider-side delivery identifier (header or body hash).</summary>
    public string DeliveryId { get; private set; } = string.Empty;

    /// <summary>Pipeline outcome label — see <see cref="DeliveryOutcomes"/>.</summary>
    public string Outcome { get; private set; } = string.Empty;

    /// <summary>Optional outcome detail (e.g. the ticket id or skip reason).</summary>
    public string? Detail { get; private set; }

    /// <summary>When the delivery was accepted.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Creates a delivery row in its just-received state.</summary>
    /// <param name="source"></param>
    /// <param name="deliveryId"></param>
    /// <param name="now"></param>
    public static IntakeDelivery Create(string source, string deliveryId, DateTimeOffset now)
    {
        return new IntakeDelivery
        {
            Id = Guid.CreateVersion7(),
            Source = source,
            DeliveryId = deliveryId,
            Outcome = string.Empty,
            Detail = null,
            ReceivedAt = now,
        };
    }

    /// <summary>Records the pipeline outcome.</summary>
    /// <param name="outcome"></param>
    /// <param name="detail"></param>
    public void SetOutcome(string outcome, string? detail)
    {
        Outcome = outcome;
        Detail = detail;
    }
}
