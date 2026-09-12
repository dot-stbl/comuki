using System.Reflection;

namespace Comuki.Shared.Bootstrap.Versioning;

/// <summary>
/// Reads <see cref="ComukiBuildInformation"/> from an assembly: the
/// informational version (<c>1.0.0+sha</c>) plus the
/// <c>ComukiBuildDate</c> / <c>ComukiBuildMode</c> metadata stamped by the
/// <c>GenerateComukiBuildMetadata</c> build hook (issue #56).
/// </summary>
public static class ComukiBuildInfo
{
    /// <summary>Assembly metadata key holding the UTC build date.</summary>
    public const string BuildDateMetadataKey = "ComukiBuildDate";

    /// <summary>Assembly metadata key holding the build mode (Debug/Release).</summary>
    public const string BuildModeMetadataKey = "ComukiBuildMode";

    /// <summary>Reads the build information of the running process's entry assembly.</summary>
    public static ComukiBuildInformation Read()
    {
        return Read(Assembly.GetEntryAssembly());
    }

    /// <summary>Reads the build information of the supplied assembly.</summary>
    /// <param name="assembly">Assembly to inspect; null yields <see cref="ComukiBuildInformation.Unknown"/>.</param>
    public static ComukiBuildInformation Read(Assembly? assembly)
    {
        if (assembly is null)
        {
            return ComukiBuildInformation.Unknown;
        }

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return ComukiBuildInformation.Unknown;
        }

        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var buildDate = ComukiBuildMetadata.Find(metadata, BuildDateMetadataKey);
        var mode = ComukiBuildMetadata.Find(metadata, BuildModeMetadataKey);

        return Compose(informational, buildDate, mode);
    }

    /// <summary>
    /// Parse core over the raw attribute values, internal for tests: splits
    /// <c>version+revision</c>, defaults the mode to <c>unknown</c>.
    /// </summary>
    /// <param name="informationalVersion">Raw AssemblyInformationalVersion value.</param>
    /// <param name="buildDate">ComukiBuildDate metadata value, or null.</param>
    /// <param name="mode">ComukiBuildMode metadata value, or null.</param>
    internal static ComukiBuildInformation Compose(string? informationalVersion, string? buildDate, string? mode)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return ComukiBuildInformation.Unknown;
        }

        var separator = informationalVersion.IndexOf('+');
        return separator < 0
            ? new ComukiBuildInformation(informationalVersion, null, ComukiBuildMetadata.Clean(buildDate), ComukiBuildMetadata.Clean(mode) ?? "unknown")
            : new ComukiBuildInformation(
                informationalVersion[..separator],
                ComukiBuildMetadata.Clean(informationalVersion[(separator + 1)..]),
                ComukiBuildMetadata.Clean(buildDate),
                ComukiBuildMetadata.Clean(mode) ?? "unknown");
    }
}

/// <summary>Metadata lookup / blank-normalisation helpers for <see cref="ComukiBuildInfo"/>.</summary>
file static class ComukiBuildMetadata
{
    public static string? Find(IEnumerable<AssemblyMetadataAttribute> metadata, string key)
    {
        return metadata.FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
