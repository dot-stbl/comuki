using System.Text.Json;
using Comuki.Modules.Projects.Domain.Settings;

namespace Comuki.Modules.Projects.Application.DomainTypes;

/// <summary>
/// Default <see cref="IProjectDomainTypeResolver"/>: parses the per-project
/// JSON map on every call (settings rows are tiny and live behind a
/// snapshot cache, so the cost is negligible), looks up the domain type
/// and falls back to the fixed default for Hybrid projects. Stateless —
/// safe as a singleton.
/// </summary>
public sealed class ProjectDomainTypeResolver : IProjectDomainTypeResolver
{
    /// <inheritdoc />
    public string ResolveProfileKey(ProjectSettings settings, string domainType)
    {
        // Trust the signature for null — compiler enforces non-null. Whitespace
        // is a programmer error and the resolver cannot make a profile key from
        // it; surface a typed ArgumentException with the parameter name.
        return string.IsNullOrWhiteSpace(domainType)
            ? throw new ArgumentException("domainType must not be null or whitespace.", nameof(domainType))
            : settings.DomainType switch
            {
                ProjectDomainType.Standard => ProjectSettings.DefaultDomainProfileKey,

                ProjectDomainType.Custom => DomainTypeRouting.ResolveCustom(settings, domainType),

                ProjectDomainType.Hybrid => DomainTypeRouting.ResolveHybrid(settings, domainType),

                _ => throw new ProjectDomainTypeNotMappedException(
                    domainType,
                    settings.DomainType.ToString(),
                    "unknown_mode"),
            };
    }
}

/// <summary>
/// Per-mode resolution helpers. Extracted to a file-scoped static class so
/// <see cref="ProjectDomainTypeResolver"/> stays a single dispatch method
/// (per <c>code-shape.md</c> §1a — no private methods in production
/// classes).
/// </summary>
file static class DomainTypeRouting
{
    /// <summary>Custom: only the JSON map; missing throws.</summary>
    /// <param name="settings"></param>
    /// <param name="domainType"></param>
    /// <returns></returns>
    public static string ResolveCustom(ProjectSettings settings, string domainType)
    {
        var map = ParseMap(settings, domainType, settings.DomainType);
        return map is not null && map.TryGetValue(domainType, out var profileKey)
            ? profileKey
            : throw new ProjectDomainTypeNotMappedException(
                domainType,
                settings.DomainType.ToString(),
                "missing");
    }

    /// <summary>Hybrid: JSON map wins when present, otherwise the default.</summary>
    /// <param name="settings"></param>
    /// <param name="domainType"></param>
    /// <returns></returns>
    public static string ResolveHybrid(ProjectSettings settings, string domainType)
    {
        return ParseMap(settings, domainType, settings.DomainType) is { } map
            && map.TryGetValue(domainType, out var profileKey)
            ? profileKey
            : ProjectSettings.DefaultDomainProfileKey;
    }

    /// <summary>
    /// Parses the per-project JSON map. Hybrid with an empty/whitespace map
    /// falls through cleanly to the default (no exception); Custom with an
    /// empty/whitespace map surfaces the standard "empty" error so the
    /// caller sees the same shape as a missing key.
    /// </summary>
    /// <param name="settings"></param>
    /// <param name="domainType"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    private static IReadOnlyDictionary<string, string>? ParseMap(
        ProjectSettings settings,
        string domainType,
        ProjectDomainType mode)
    {
        var json = settings.CustomDomainTypesJson;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web);
                return parsed is null
                    ? throw new ProjectDomainTypeNotMappedException(
                        domainType,
                        mode.ToString(),
                        "malformed")
                    : parsed;
            }
            catch (JsonException)
            {
                throw new ProjectDomainTypeNotMappedException(
                    domainType,
                    mode.ToString(),
                    "malformed");
            }
        }

        return mode == ProjectDomainType.Hybrid
            ? null
            : throw new ProjectDomainTypeNotMappedException(
                domainType,
                mode.ToString(),
                "empty");
    }
}
