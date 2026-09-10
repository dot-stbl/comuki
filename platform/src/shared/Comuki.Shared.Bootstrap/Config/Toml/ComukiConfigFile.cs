namespace Comuki.Shared.Bootstrap.Config.Toml;

/// <summary>
/// config.toml discovery (issue #54): <c>COMUKI_CONFIG_PATH</c> →
/// <c>./config.toml</c> in the working directory →
/// <c>/etc/comuki/config.toml</c> (Linux only). No candidate found means
/// no file — an empty configuration, never an error: build-time OpenAPI
/// generation boots the hosts without any config.toml present and must
/// keep working.
/// </summary>
public static class ComukiConfigFile
{
    /// <summary>Env var holding an explicit config.toml path.</summary>
    public const string PathEnvironmentVariable = "COMUKI_CONFIG_PATH";

    /// <summary>The file name probed in the working directory.</summary>
    public const string WorkingDirectoryFileName = "config.toml";

    /// <summary>The system-wide location probed on Linux only.</summary>
    public const string LinuxSystemPath = "/etc/comuki/config.toml";

    /// <summary>Finds the effective config.toml, or null when no candidate exists.</summary>
    public static string? Find()
    {
        return Find(Environment.GetEnvironmentVariable, Directory.GetCurrentDirectory, OperatingSystem.IsLinux, File.Exists);
    }

    /// <summary>Search core, parameterised for tests.</summary>
    /// <param name="lookup">Env-var accessor (name → value or null).</param>
    /// <param name="currentDirectory">Working-directory accessor.</param>
    /// <param name="isLinux">True when the system-wide path applies.</param>
    /// <param name="fileExists">File-existence probe (injected for tests).</param>
    /// <returns>First existing candidate in the documented order, else null.</returns>
    internal static string? Find(
        Func<string, string?> lookup,
        Func<string> currentDirectory,
        Func<bool> isLinux,
        Func<string, bool> fileExists)
    {
        return ComukiConfigFileCandidates
            .Build(lookup, currentDirectory, isLinux)
            .FirstOrDefault(fileExists);
    }
}

/// <summary>Ordered candidate list for the config.toml search chain.</summary>
file static class ComukiConfigFileCandidates
{
    public static IEnumerable<string> Build(Func<string, string?> lookup, Func<string> currentDirectory, Func<bool> isLinux)
    {
        var explicitPath = lookup(ComukiConfigFile.PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        yield return Path.Combine(currentDirectory(), ComukiConfigFile.WorkingDirectoryFileName);

        if (isLinux())
        {
            yield return ComukiConfigFile.LinuxSystemPath;
        }
    }
}
