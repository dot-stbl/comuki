namespace Comuki.Migrator.Unit;

/// <summary>
/// Test-only: snapshots the named env vars, sets the requested values, and
/// restores the originals on <see cref="Dispose"/>. The Migrator's
/// <c>ConnectionStringSource.TryResolve</c> reads process-global env vars,
/// so this scope pairs with the <c>[Collection("MigratorEnvSafe")]</c>
/// gate to keep these tests deterministic.
/// </summary>
internal sealed class EnvVarScope((string key, string? original)[] snapshot) : IDisposable
{
    /// <summary>Set the named env vars for the lifetime of the returned scope.</summary>
    public static EnvVarScope Set(params (string key, string? value)[] entries)
    {
        var saved = entries
            .Select(static entry => (entry.key, original: Environment.GetEnvironmentVariable(entry.key)))
            .ToArray();
        foreach (var (key, value) in entries)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        return new EnvVarScope(saved);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (key, original) in snapshot)
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }
}
