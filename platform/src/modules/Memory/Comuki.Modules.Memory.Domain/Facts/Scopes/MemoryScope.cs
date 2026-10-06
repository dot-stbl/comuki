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

    /// <summary>
    ///     Facts scoped to one mission. Mission facts are visible only to a
    ///     caller that supplies the participating mission id via the query
    ///     port — without it, the query filter excludes the row (fail-closed).
    /// </summary>
    Mission = 4,
}
