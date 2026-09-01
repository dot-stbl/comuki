namespace Comuki.Modules.Memory.Domain.Learning;

/// <summary>Review state of a learning candidate.</summary>
public enum LearningStatus
{
    /// <summary>Waiting for a human decision.</summary>
    Pending = 1,

    /// <summary>Approved — a PR into the client's git (profiles/rules) follows.</summary>
    Approved = 2,

    /// <summary>Rejected — kept for the repeat counter history, never promoted.</summary>
    Rejected = 3,
}
