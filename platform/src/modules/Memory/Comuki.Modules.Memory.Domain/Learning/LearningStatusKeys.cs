namespace Comuki.Modules.Memory.Domain.Learning;

/// <summary>Stable wire keys for <see cref="LearningStatus"/>.</summary>
public static class LearningStatusKeys
{
    /// <summary>Key of <see cref="LearningStatus.Pending"/>.</summary>
    public const string Pending = "pending";

    /// <summary>Key of <see cref="LearningStatus.Approved"/>.</summary>
    public const string Approved = "approved";

    /// <summary>Key of <see cref="LearningStatus.Rejected"/>.</summary>
    public const string Rejected = "rejected";

    /// <summary>Maps a status to its wire key.</summary>
    /// <param name="status"></param>
    public static string Key(LearningStatus status)
    {
        return status switch
        {
            LearningStatus.Pending => Pending,
            LearningStatus.Approved => Approved,
            LearningStatus.Rejected => Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    /// <param name="key"></param>
    public static LearningStatus? Parse(string key)
    {
        return key switch
        {
            Pending => LearningStatus.Pending,
            Approved => LearningStatus.Approved,
            Rejected => LearningStatus.Rejected,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — the EF converter path (expression trees cannot inline throws).</summary>
    /// <param name="key"></param>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static LearningStatus ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown learning status key '{key}'");
    }
}
