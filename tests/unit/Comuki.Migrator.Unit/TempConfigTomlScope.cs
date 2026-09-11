using Comuki.Shared.Bootstrap.Config.Toml;

namespace Comuki.Migrator.Unit;

/// <summary>
/// Test-only: installs a temporary <c>config.toml</c> and points
/// <c>COMUKI_CONFIG_PATH</c> at it (the path <c>ConnectionStringSource</c>
/// resolves the Comuki connection string through); cleans both up on
/// dispose. Paired with <see cref="EnvVarScope"/> via the
/// <c>[Collection(nameof(MigratorEnvSafeCollection))]</c> gate so the state is invariant
/// per test.
/// </summary>
internal sealed class TempConfigTomlScope(string path, string? originalConfigPath) : IDisposable
{
    /// <summary>Install the supplied TOML content as the scoped config.toml.</summary>
    public static TempConfigTomlScope Install(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comuki-migrator-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content);

        var original = Environment.GetEnvironmentVariable(ComukiConfigFile.PathEnvironmentVariable);
        Environment.SetEnvironmentVariable(ComukiConfigFile.PathEnvironmentVariable, path);

        return new TempConfigTomlScope(path, original);
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
