namespace Comuki.Modules.Memory.Domain.Facts.Kinds;

/// <summary>Fact lifetime: standing decisions vs task-scoped ephemeral notes.</summary>
public enum MemoryFactKind
{
    /// <summary>Long-lived decisions and preferences; no TTL.</summary>
    Standing = 1,

    /// <summary>Task-scoped note; swept after <see cref="MemoryFactPolicy.EphemeralTtl"/>.</summary>
    Ephemeral = 2,

    /// <summary>
    ///     A blackboard finding surfaced by a worker during a mission. Carries
    ///     a composite <c>TopicKey = (MissionId, workerKey, fingerprint)</c>
    ///     so supersede is keyed per worker output, not per mission. Lives
    ///     alongside the existing kinds because it follows the same
    ///     write/read/sweep mechanics — the kind is metadata, not a
    ///     separate aggregate.
    /// </summary>
    BlackboardFinding = 3,
}
