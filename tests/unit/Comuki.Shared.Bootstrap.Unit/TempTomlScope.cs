using Comuki.Shared.Bootstrap.Config.Toml;

namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// Test-only: writes a temporary config.toml, points
/// <c>COMUKI_CONFIG_PATH</c> at it, and cleans both up on dispose.
/// Removes the original env value when it lived at the temp path.
/// </summary>
internal sealed class TempTomlScope(string path, string? originalConfigPath) : IDisposable
{
    /// <summary>Install the supplied TOML content as the scoped config.toml.</summary>
    public static TempTomlScope Install(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comuki-test-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content);

        var original = Environment.GetEnvironmentVariable(ComukiConfigFile.PathEnvironmentVariable);
        Environment.SetEnvironmentVariable(ComukiConfigFile.PathEnvironmentVariable, path);

        return new TempTomlScope(path, original);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Environment.SetEnvironmentVariable(ComukiConfigFile.PathEnvironmentVariable, originalConfigPath);
    }
}
