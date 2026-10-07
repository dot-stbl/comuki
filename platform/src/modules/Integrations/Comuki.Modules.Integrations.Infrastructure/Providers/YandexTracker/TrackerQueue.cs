namespace Comuki.Modules.Integrations.Infrastructure.Providers.YandexTracker;

/// <summary>Tracker queue wire shape.</summary>
public sealed record TrackerQueue
{
    /// <summary>Queue key.</summary>
    public string Key { get; init; } = string.Empty;
}
