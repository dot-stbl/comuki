using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Orchestration.Options;

/// <summary>
/// Durable outbox dispatch policy: how often the dispatcher sweep runs,
/// the maximum number of rows it claims per sweep, and the bounded retry
/// budget before a single message is dead-lettered.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Config section: <c>Orchestration:Outbox</c>.</summary>
    public const string SectionName = "Orchestration:Outbox";

    /// <summary>How often <c>OutboxDispatcherComukiWorker</c> runs one dispatch sweep.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan DispatchInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum number of undispatched rows a single sweep claims.</summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>Delivery attempts before the row is dead-lettered.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;
}
