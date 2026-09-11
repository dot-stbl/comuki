namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// Stable wire values of <see cref="MessagePart.ToolPart.Status"/>. The
/// console renders one affordance per value (spinner / observation /
/// failure), so the set is closed and matches the console's own
/// <c>ToolStatus</c> vocabulary.
/// </summary>
public static class ToolPartStatuses
{
    /// <summary>The call was issued and has not come back yet.</summary>
    public const string Running = "running";

    /// <summary>The call came back with an observation.</summary>
    public const string Success = "success";

    /// <summary>The call failed; <c>outputJson</c> carries the failure payload when there is one.</summary>
    public const string Failed = "failed";

    /// <summary>Parses a wire value; null when unknown.</summary>
    /// <param name="value">Candidate status string.</param>
    public static string? Parse(string value)
    {
        return value switch
        {
            Running => Running,
            Success => Success,
            Failed => Failed,
            _ => null,
        };
    }
}
