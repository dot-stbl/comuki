namespace Comuki.Shared.Bootstrap.Unit;

/// <summary>
/// Test-only: writes a temporary config.toml, points
/// <c>COMUKI_CONFIG_PATH</c> at it, and cleans both up on dispose.
/// Removes the original env value when it lived at the temp path.
/// </summary>
internal sealed class TempTomlScope : IDisposable
{
    private readonly string path;
    private readonly string? originalConfigPath;

    private TempTomlScope(string path, string? originalConfigPath)
    {
        this.path = path;
        this.originalConfigPath = originalConfigPath;
    }

    /// <summary>Install the supplied TOML content as the scoped config.toml.</summary>
    public static TempTomlScope Install(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comuki-test-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content);

        var original = Environment.GetEnvironmentVariable("COMUKI_CONFIG_PATH");
        Environment.SetEnvironmentVariable("COMUKI_CONFIG_PATH", path);

        return new TempTomlScope(path, original);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Environment.SetEnvironmentVariable("COMUKI_CONFIG_PATH", originalConfigPath);
    }
}
