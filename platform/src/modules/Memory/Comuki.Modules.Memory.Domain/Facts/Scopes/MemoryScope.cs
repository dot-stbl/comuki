namespace Comuki.Modules.Memory.Domain.Facts.Scopes;

/// <summary>Who a memory fact belongs to. Wire key: <see cref="MemoryScopeKeys"/>.</summary>
public enum MemoryScope
{
    /// <summary>Facts of one user across projects.</summary>
    User = 1,

    /// <summary>Facts of one project.</summary>
    Project = 2,

    /// <summary>Platform-wide facts.</summary>
    Global = 3,
}
