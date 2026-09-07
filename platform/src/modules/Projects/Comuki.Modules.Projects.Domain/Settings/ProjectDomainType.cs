namespace Comuki.Modules.Projects.Domain.Settings;

/// <summary>
/// Per-project routing mode for user-facing domain types
/// (<c>code</c>, <c>data</c>, <c>infra</c>, <c>research</c>, …).
/// A Standard project sends every domain through the fixed default
/// profile. A Custom project routes only via the
/// <see cref="ProjectSettings.CustomDomainTypesJson"/> map. A Hybrid
/// project tries the default first, falls back to the JSON map.
/// </summary>
public enum ProjectDomainType
{
    /// <summary>
    /// Every domain is routed to the fixed default profile
    /// (<c>implement</c>). The custom JSON map is ignored.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Every domain is routed through the custom JSON map. The default
    /// is ignored. A missing entry is a typed error.
    /// </summary>
    Custom = 1,

    /// <summary>
    /// The custom JSON map wins when it has the domain; otherwise the
    /// fixed default is used. A missing entry in both is a typed error.
    /// </summary>
    Hybrid = 2,
}
