namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>Tracker user wire shape.</summary>
public sealed record TrackerUser
{
    /// <summary>User login.</summary>
    public string Login { get; init; } = string.Empty;
}
