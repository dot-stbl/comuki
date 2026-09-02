using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Intake.Application.Options;

/// <summary>
/// Intake runtime knobs: the run URL base used in sync-back comments and
/// the bridge/sync worker cadence. Bound from the <c>Intake</c> section.
/// </summary>
public sealed class IntakeOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Intake";

    /// <summary>Public base URL of the dashboard/host — sync-back comments link runs under it.</summary>
    [Required]
    [Url]
    public Uri PublicBaseUrl { get; init; } = new("http://localhost:17170");

    /// <summary>How often the bridge scans claimed tickets for terminal runs and drains the outbox.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan BridgeInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Sync-back attempt budget per job.</summary>
    [Range(1, 20)]
    public int SyncMaxAttempts { get; init; } = 5;

    /// <summary>Base backoff of the exponential sync-back retry (doubles per attempt).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan SyncBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound of one bridge batch (tickets scanned and jobs drained per cycle).</summary>
    [Range(1, 500)]
    public int BridgeBatchSize { get; init; } = 100;

    /// <summary>Page size of the inbox catalog fetches.</summary>
    [Range(1, 100)]
    public int CatalogPageSize { get; init; } = 25;

    /// <summary>Upper bound of the pending-inbox list.</summary>
    [Range(1, 200)]
    public int InboxListLimit { get; init; } = 50;
}
