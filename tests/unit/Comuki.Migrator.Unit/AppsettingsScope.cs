namespace Comuki.Migrator.Unit;

/// <summary>
/// Test-only: installs a temporary <c>appsettings.json</c> in
/// <see cref="AppContext.BaseDirectory"/> (where <c>ConnectionStringSource</c>
/// resolves the <c>Comuki</c> connection string from) and restores the
/// original on <see cref="Dispose"/>. Paired with
/// <see cref="EnvVarScope"/> via the <c>[Collection("MigratorEnvSafe")]</c>
/// gate so the file state is invariant per test.
/// </summary>
internal sealed class AppsettingsScope(string path, string? originalContent) : IDisposable
{
    private static readonly string binPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>Install the supplied JSON content as the test bin's appsettings.json.</summary>
    public static AppsettingsScope Install(string content)
    {
        var original = File.Exists(binPath) ? File.ReadAllText(binPath) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(binPath)!);
        File.WriteAllText(binPath, content);
        return new AppsettingsScope(binPath, original);
    }

    /// <summary>Remove the test bin's appsettings.json (if any).</summary>
    public static AppsettingsScope Remove()
    {
        var original = File.Exists(binPath) ? File.ReadAllText(binPath) : null;
        if (original is not null)
        {
            File.Delete(binPath);
        }

        return new AppsettingsScope(binPath, original);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (originalContent is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            File.WriteAllText(path, originalContent);
        }
    }
}
