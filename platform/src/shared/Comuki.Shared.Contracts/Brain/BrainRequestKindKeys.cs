namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// Stable wire keys for the brain request kind — the discriminating first
/// field of <see cref="BrainRequest"/>. The same keys appear inside
/// <c>contextJson</c> consumers and the journal.
/// </summary>
public static class BrainRequestKindKeys
{
    /// <summary>Decompose the task into an executable plan (finalJson carries the plan).</summary>
    public const string Plan = "plan";

    /// <summary>Write a worker brief for one work item.</summary>
    public const string Brief = "brief";

    /// <summary>Repair a failing work item from its report.</summary>
    public const string Repair = "repair";

    /// <summary>Answer a question over the platform tools.</summary>
    public const string Answer = "answer";

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static string? Parse(string key)
    {
        return key switch
        {
            Plan => Plan,
            Brief => Brief,
            Repair => Repair,
            Answer => Answer,
            _ => null,
        };
    }
}
