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
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);

        return settings.DomainType switch
        {
            ProjectDomainType.Standard => ProjectSettings.DefaultDomainProfileKey,

            ProjectDomainType.Custom => ResolveCustom(settings, domainType),

            ProjectDomainType.Hybrid => ResolveHybrid(settings, domainType),

            _ => throw new ProjectDomainTypeNotMappedException(
                domainType,
                settings.DomainType.ToString(),
                "unknown_mode"),
        };
    }

    private static string ResolveCustom(ProjectSettings settings, string domainType)
    {
        var map = ParseMap(settings, domainType, settings.DomainType);
        return map is not null && map.TryGetValue(domainType, out var profileKey)
            ? profileKey
            : throw new ProjectDomainTypeNotMappedException(
                domainType,
                settings.DomainType.ToString(),
                "missing");
    }

    private static string ResolveHybrid(ProjectSettings settings, string domainType)
    {
        // Hybrid wins when the map has it — otherwise fall back to the default.
        return ParseMap(settings, domainType, settings.DomainType) is { } map
            && map.TryGetValue(domainType, out var profileKey)
            ? profileKey
            : ProjectSettings.DefaultDomainProfileKey;
    }

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

        // Hybrid without a map falls through cleanly to the default.
        // Custom without a map cannot resolve anything — surface the
        // standard "empty" error so the caller gets the same shape.
        return mode == ProjectDomainType.Hybrid
            ? null
            : throw new ProjectDomainTypeNotMappedException(
                domainType,
                mode.ToString(),
                "empty");
    }
}
