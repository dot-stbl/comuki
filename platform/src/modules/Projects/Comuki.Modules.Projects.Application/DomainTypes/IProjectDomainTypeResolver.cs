using Comuki.Modules.Projects.Domain.Settings;

namespace Comuki.Modules.Projects.Application.DomainTypes;

/// <summary>
/// Maps a user-facing domain type (<c>code</c>, <c>data</c>, <c>infra</c>,
/// <c>research</c>, …) to a control-plane profile key
/// (<c>implement</c>, <c>docs-writer</c>, …) for one project. The
/// resolution honours the project's declared
/// <see cref="ProjectSettings.DomainType"/> mode:
/// <list type="bullet">
///     <item><see cref="ProjectDomainType.Standard"/> — the fixed default
///     (<see cref="ProjectSettings.DefaultDomainProfileKey"/>) is returned
///     for every domain; the JSON map is ignored.</item>
///     <item><see cref="ProjectDomainType.Custom"/> — only the JSON map is
///     consulted; a missing key throws.</item>
///     <item><see cref="ProjectDomainType.Hybrid"/> — the JSON map wins
///     when it has the key, otherwise the fixed default; both miss throws.</item>
/// </list>
/// Singleton — the implementation is pure (settings + domain in, profile
/// key out) and the dictionary is shared across requests.
/// </summary>
public interface IProjectDomainTypeResolver
{
    /// <summary>
    /// Resolves a domain type to the profile key that should handle it for
    /// this project.
    /// </summary>
    /// <param name="settings">Project settings that own the routing mode and the JSON map.</param>
    /// <param name="domainType">User-facing domain type (e.g. <c>code</c>).</param>
    /// <returns>Control-plane profile key (e.g. <c>implement</c>).</returns>
    /// <exception cref="ProjectDomainTypeNotMappedException">
    /// The domain type cannot be resolved under the project's declared mode
    /// (missing from the JSON map for a Custom project; missing from both
    /// the JSON map and the default for a Hybrid project; or the JSON map
    /// is malformed).
    /// </exception>
    public string ResolveProfileKey(ProjectSettings settings, string domainType);
}
