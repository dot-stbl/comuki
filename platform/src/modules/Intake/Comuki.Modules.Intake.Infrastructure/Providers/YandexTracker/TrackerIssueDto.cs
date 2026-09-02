namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>Yandex Tracker issue wire DTO — tolerant: unknown fields ignored.</summary>
public sealed record TrackerIssueDto
{
    /// <summary>Issue key (e.g. COMUKI-5).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Issue summary (title).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Issue description; may arrive null.</summary>
    public string? Description { get; init; }

    /// <summary>Queue sub-object.</summary>
    public TrackerQueueDto? Queue { get; init; }

    /// <summary>Queue key.</summary>
    public string QueueKey => Queue?.Key ?? string.Empty;

    /// <summary>Creator sub-object.</summary>
    public TrackerUserDto? CreatedBy { get; init; }

    /// <summary>Creator login.</summary>
    public string CreatedByLogin => CreatedBy?.Login ?? string.Empty;

    /// <summary>Browsable issue URL (self link of the API).</summary>
    public string Self { get; init; } = string.Empty;

    /// <summary>Issue tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Tracker queue wire shape.</summary>
public sealed record TrackerQueueDto
{
    /// <summary>Queue key.</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>Tracker user wire shape.</summary>
public sealed record TrackerUserDto
{
    /// <summary>User login.</summary>
    public string Login { get; init; } = string.Empty;
}

/// <summary>Search request body (HQL query).</summary>
public sealed record TrackerSearchBody(string Query);

/// <summary>Comment request body.</summary>
public sealed record TrackerCommentBody(string Text);
