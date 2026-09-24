namespace Comuki.Engine.Orchestration.Domain.Outbox;

/// <summary>
/// Durable terminal-event outbox row. Enqueued in the same transaction as
/// the aggregate change it reports (the caller's <c>SaveChangesAsync</c>
/// commits both atomically); delivered independently of the best-effort
/// realtime SignalR broadcast that fans out the same terminal event. The
/// delivery outcome is timestamp-driven, not a status enum —
/// <see cref="DispatchedAt"/> / <see cref="DeadLetteredAt"/> null-ness IS
/// the state.
/// </summary>
public sealed class OutboxMessage
{
    internal OutboxMessage()
    {
    }

    /// <summary>Message id (UUIDv7, client-side).</summary>
    public Guid Id { get; private set; }

    /// <summary>Contract name — a stable dot.case string like <c>orchestration.run.terminated.v1</c>.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Raw JSON payload (<c>jsonb</c> column).</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>When the message was enqueued (commit order key for the dispatch sweep).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the publisher successfully delivered the message; <c>null</c> while pending.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>How many dispatch attempts have failed so far; starts at 0.</summary>
    public int Attempts { get; private set; }

    /// <summary>The text of the most recent delivery failure; <c>null</c> until the first failure.</summary>
    public string? LastError { get; private set; }

    /// <summary>When the row was dead-lettered (attempts exhausted); <c>null</c> while not dead-lettered.</summary>
    public DateTimeOffset? DeadLetteredAt { get; private set; }

    /// <summary><c>true</c> when the publisher has acknowledged delivery.</summary>
    public bool IsDispatched => DispatchedAt is not null;

    /// <summary><c>true</c> when the bounded retry budget was exhausted.</summary>
    public bool IsDeadLettered => DeadLetteredAt is not null;

    /// <summary>
    /// Stages a new message for durable at-least-once delivery. Type and
    /// payload must be non-empty; <paramref name="now"/> becomes both the
    /// enqueue timestamp and the row's commit-order key.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="payloadJson"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static OutboxMessage Create(string type, string payloadJson, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("outbox message type must not be empty", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("outbox message payload must not be empty", nameof(payloadJson));
        }

        var id = Guid.CreateVersion7();
        return new OutboxMessage
        {
            Id = id,
            Type = type,
            Payload = payloadJson,
            CreatedAt = now,
        };
    }

    /// <summary>Records a successful delivery; transitions the row to the dispatched state.</summary>
    /// <param name="now"></param>
    public void MarkDispatched(DateTimeOffset now)
    {
        DispatchedAt = now;
    }

    /// <summary>
    /// Records one failed delivery attempt: bumps <see cref="Attempts"/>,
    /// stores the error text, and dead-letters the row once the bounded
    /// retry budget is exhausted.
    /// </summary>
    /// <param name="error"></param>
    /// <param name="now"></param>
    /// <param name="maxAttempts"></param>
    /// <exception cref="ArgumentException"></exception>
    public void RecordFailure(string error, DateTimeOffset now, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("failure error must not be empty", nameof(error));
        }

        Attempts++;
        LastError = error;
        if (Attempts >= maxAttempts)
        {
            DeadLetteredAt = now;
        }
    }
}
