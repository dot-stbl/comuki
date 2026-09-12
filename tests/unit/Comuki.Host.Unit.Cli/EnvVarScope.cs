namespace Comuki.Host.Unit.Cli;

/// <summary>One env-var assignment for <see cref="EnvVarScope.Set"/>.</summary>
internal sealed record EnvVarEntry(string Key, string? Value);

/// <summary>
/// Test-only: snapshots the named env vars, sets the requested values, and
/// restores the originals on dispose. The comuki CLI branches read
/// process-global env vars (COMUKI_CONFIG_PATH, COMUKI_*), so this scope
/// pairs with the <c>[Collection(nameof(CliEnvSafeCollection))]</c> gate to
/// keep these tests deterministic.
/// </summary>
internal sealed class EnvVarScope(EnvVarEntry[] snapshot) : IDisposable
{
    /// <summary>Set the named env vars for the lifetime of the returned scope.</summary>
    public static EnvVarScope Set(params EnvVarEntry[] entries)
    {
        var saved = entries
            .Select(static entry => new EnvVarEntry(entry.Key, Environment.GetEnvironmentVariable(entry.Key)))
            .ToArray();
        foreach (var entry in entries)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }

        return new EnvVarScope(saved);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var entry in snapshot)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }
}
