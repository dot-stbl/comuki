namespace Comuki.Modules.Work.Domain.Sources;

/// <summary>
/// The kind of source a <see cref="WorkTaskSourceRef"/> carries —
/// which tracker surface a Task's source link came from. Wire form
/// is the PascalCase member name; EF stores the smart-type via
/// <c>HasConversion&lt;string&gt;</c>.
/// </summary>
public readonly record struct WorkTaskSourceKind
{
    private readonly string? value;

    private WorkTaskSourceKind(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — <c>default(WorkTaskSourceKind)</c>; not a working kind.</summary>
    public static WorkTaskSourceKind Unspecified { get; }

    /// <summary>The Task was created by a Native UI surface (no external tracker).</summary>
    public static WorkTaskSourceKind Native { get; } = new("Native");

    /// <summary>The Task was created by an inbound GitHub issue / PR.</summary>
    public static WorkTaskSourceKind GitHub { get; } = new("GitHub");

    /// <summary>The Task was created by an inbound GitLab issue / MR.</summary>
    public static WorkTaskSourceKind GitLab { get; } = new("GitLab");

    /// <summary>The Task was created by an inbound Jira issue.</summary>
    public static WorkTaskSourceKind Jira { get; } = new("Jira");

    /// <summary>The Task was created by an inbound Yandex Tracker issue.</summary>
    public static WorkTaskSourceKind YandexTracker { get; } = new("YandexTracker");

    /// <summary>Wire-form string — the exact PascalCase text EF stores.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    public static WorkTaskSourceKind FromWire(string wire)
    {
        return wire switch
        {
            nameof(Native) => Native,
            nameof(GitHub) => GitHub,
            nameof(GitLab) => GitLab,
            nameof(Jira) => Jira,
            nameof(YandexTracker) => YandexTracker,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(WorkTaskSourceKind)} value."),
        };
    }

    /// <summary>Try-parse variant — returns <c>true</c> when the wire string maps to a known kind; the <c>out</c> argument is <see cref="Unspecified"/> when unknown.</summary>
    public static bool TryFromWire(string wire, out WorkTaskSourceKind kind)
    {
        switch (wire)
        {
            case nameof(Native): kind = Native; return true;
            case nameof(GitHub): kind = GitHub; return true;
            case nameof(GitLab): kind = GitLab; return true;
            case nameof(Jira): kind = Jira; return true;
            case nameof(YandexTracker): kind = YandexTracker; return true;
            default: kind = Unspecified; return false;
        }
    }
}
