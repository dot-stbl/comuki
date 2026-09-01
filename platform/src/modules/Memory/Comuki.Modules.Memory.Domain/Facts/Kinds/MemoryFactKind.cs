namespace Comuki.Modules.Memory.Domain.Facts.Kinds;

/// <summary>Fact lifetime: standing decisions vs task-scoped ephemeral notes.</summary>
public enum MemoryFactKind
{
    /// <summary>Long-lived decisions and preferences; no TTL.</summary>
    Standing = 1,

    /// <summary>Task-scoped note; swept after <see cref="MemoryFactPolicy.EphemeralTtl"/>.</summary>
    Ephemeral = 2,
}
