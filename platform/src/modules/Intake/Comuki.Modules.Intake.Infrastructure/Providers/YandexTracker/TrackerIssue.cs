namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>Yandex Tracker issue wire shape — tolerant: unknown fields ignored.</summary>
public sealed record TrackerIssue
{
    /// <summary>Issue key (e.g. COMUKI-5).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Issue summary (title).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Issue description; may arrive null.</summary>
    public string? Description { get; init; }

    /// <summary>Queue sub-object.</summary>
    public TrackerQueue? Queue { get; init; }

    /// <summary>Queue key.</summary>
    public string QueueKey => Queue?.Key ?? string.Empty;

    /// <summary>Creator sub-object.</summary>
    public TrackerUser? CreatedBy { get; init; }

    /// <summary>Creator login.</summary>
    public string CreatedByLogin => CreatedBy?.Login ?? string.Empty;

    /// <summary>Browsable issue URL (self link of the API).</summary>
    public string Self { get; init; } = string.Empty;

    /// <summary>Issue tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
