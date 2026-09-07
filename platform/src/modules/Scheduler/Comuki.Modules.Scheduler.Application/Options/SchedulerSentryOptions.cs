namespace Comuki.Modules.Scheduler.Application.Options;

/// <summary>
/// Sentry side-channel settings for the scheduler dispatcher.
/// <see cref="Sentry:Dsn"/> binds as <c>Scheduler:Sentry:Dsn</c>. When
/// the DSN is null or whitespace the <c>SentrySchedulerObserver</c> is
/// a no-op and the host skips <c>SentrySdk.Init</c> entirely — both the
/// wire egress and the SDK footprint stay out of the process.
/// </summary>
public sealed class SchedulerSentryOptions
{
    /// <summary>Nested config section the host binds from (<c>Scheduler:Sentry</c>).</summary>
    public const string SectionName = "Scheduler:Sentry";

    /// <summary>
    /// Sentry DSN the dispatcher sends its fire events to. Null or
    /// whitespace disables the side-channel — the observer is registered
    /// either way, but its body short-circuits to a no-op.
    /// </summary>
    public string? Dsn { get; init; }

    /// <summary>
    /// Optional Sentry environment (production / staging). When set, the
    /// host passes it to <c>SentrySdk.Init</c> so events tagged with
    /// <c>environment</c> land in the matching project stream.
    /// </summary>
    public string? Environment { get; init; }
}
