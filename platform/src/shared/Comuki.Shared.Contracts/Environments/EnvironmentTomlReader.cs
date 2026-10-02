using Tomlyn.Model;

namespace Comuki.Shared.Contracts.Environments;

/// <summary>
/// Field-level readers for <see cref="EnvironmentToml.TryParse"/>: one
/// TryRead per top-level key of the closed schema, each appending
/// deterministic error entries and never throwing. Separate type so the
/// parser itself stays orchestration-only (the family shape of
/// <c>PlanJson</c> / <c>MessagePartsJson</c>: parse entry point plus
/// dedicated reader/validator types, no private helpers).
/// </summary>
internal static class EnvironmentTomlReader
{
    /// <summary>The only top-level keys schema 1 accepts; anything else is a closed-schema violation.</summary>
    public static readonly IReadOnlySet<string> AllowedTopLevelKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "schema", "class", "runtime", "restore", "mounts", "verify",
    };

    private static readonly IReadOnlySet<string> allowedRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "linux", "windows",
    };

    /// <summary>Reads and validates the <c>schema</c> key: required integer equal to the supported version.</summary>
    public static bool TryReadSchema(TomlTable root, List<string> errors, out int schema)
    {
        schema = 0;
        if (!root.TryGetValue("schema", out var raw))
        {
            errors.Add("environment.toml is missing required key 'schema'.");
            return false;
        }

        if (raw is not long longSchema)
        {
            errors.Add($"environment.toml 'schema' must be an integer, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        schema = (int)longSchema;
        if (schema != EnvironmentToml.SupportedSchema)
        {
            errors.Add($"environment.toml 'schema' must be {EnvironmentToml.SupportedSchema}, got {schema}.");
            return false;
        }

        return true;
    }

    /// <summary>Reads and validates the <c>class</c> key: required non-empty string (catalog id).</summary>
    public static bool TryReadClass(TomlTable root, List<string> errors, out string classId)
    {
        classId = string.Empty;
        if (!root.TryGetValue("class", out var raw))
        {
            errors.Add("environment.toml is missing required key 'class'.");
            return false;
        }

        if (raw is not string text)
        {
            errors.Add($"environment.toml 'class' must be a string, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add("environment.toml 'class' must not be empty.");
            return false;
        }

        classId = text;
        return true;
    }

    /// <summary>Reads and validates the <c>runtime</c> key: required <c>linux</c> or <c>windows</c>.</summary>
    public static bool TryReadRuntime(TomlTable root, List<string> errors, out string runtime)
    {
        runtime = string.Empty;
        if (!root.TryGetValue("runtime", out var raw))
        {
            errors.Add("environment.toml is missing required key 'runtime'.");
            return false;
        }

        if (raw is not string text)
        {
            errors.Add($"environment.toml 'runtime' must be a string, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        if (!allowedRuntimes.Contains(text))
        {
            errors.Add($"environment.toml 'runtime' must be 'linux' or 'windows', got '{text}'.");
            return false;
        }

        runtime = text;
        return true;
    }

    /// <summary>
    /// Reads the optional <c>[restore]</c> table; scalar values normalize to
    /// single-item lists so callers see one shape.
    /// </summary>
    public static bool TryReadRestore(TomlTable root, List<string> errors, out IReadOnlyDictionary<string, IReadOnlyList<string>> restore)
    {
        restore = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (!root.TryGetValue("restore", out var raw))
        {
            return true;
        }

        if (raw is not TomlTable table)
        {
            errors.Add($"environment.toml 'restore' must be a table, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        var parsed = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (!TryReadRestoreValue(key, value, errors, out var list))
            {
                return false;
            }

            parsed[key] = list;
        }

        restore = parsed;
        return true;
    }

    /// <summary>Reads one <c>[restore]</c> entry: a string or an array of strings; any other type is an error.</summary>
    public static bool TryReadRestoreValue(string key, object? value, List<string> errors, out IReadOnlyList<string> list)
    {
        list = [];

        switch (value)
        {
            case string single:
                list = [single];
                return true;

            case TomlArray array:
                var items = new List<string>(array.Count);
                foreach (var item in array)
                {
                    if (item is not string entry)
                    {
                        errors.Add($"environment.toml 'restore.{key}' array entries must be strings, got '{item?.GetType().Name ?? "null"}'.");
                        return false;
                    }

                    items.Add(entry);
                }

                list = items;
                return true;

            case long number:
                errors.Add($"environment.toml 'restore.{key}' must be a string or array of strings, got integer '{number}'.");
                return false;

            case bool flag:
                errors.Add($"environment.toml 'restore.{key}' must be a string or array of strings, got boolean '{flag}'.");
                return false;

            case null:
                errors.Add($"environment.toml 'restore.{key}' must not be null.");
                return false;

            default:
                errors.Add($"environment.toml 'restore.{key}' must be a string or array of strings, got '{value.GetType().Name}'.");
                return false;
        }
    }

    /// <summary>Reads the optional <c>[mounts]</c> table: every value must be a string.</summary>
    public static bool TryReadMounts(TomlTable root, List<string> errors, out IReadOnlyDictionary<string, string> mounts)
    {
        mounts = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetValue("mounts", out var raw))
        {
            return true;
        }

        if (raw is not TomlTable table)
        {
            errors.Add($"environment.toml 'mounts' must be a table, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (value is not string source)
            {
                errors.Add($"environment.toml 'mounts.{key}' must be a string, got '{value?.GetType().Name ?? "null"}'.");
                return false;
            }

            parsed[key] = source;
        }

        mounts = parsed;
        return true;
    }

    /// <summary>
    /// Reads the optional <c>[verify]</c> table (isolate-verifier-runtime 1.1).
    /// Each value is normalised the same way <see cref="TryReadRestore"/> does
    /// (a scalar becomes a one-element list) so the runner sees one shape.
    /// The table may be absent — in which case <paramref name="verify"/> is an
    /// empty dictionary and the runner treats verify as a no-op for the slot.
    /// </summary>
    public static bool TryReadVerify(TomlTable root, List<string> errors, out IReadOnlyDictionary<string, IReadOnlyList<string>> verify)
    {
        verify = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (!root.TryGetValue("verify", out var raw))
        {
            return true;
        }

        if (raw is not TomlTable table)
        {
            errors.Add($"environment.toml 'verify' must be a table, got '{raw?.GetType().Name ?? "null"}'.");
            return false;
        }

        var parsed = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (!TryReadRestoreValue(key, value, errors, out var list))
            {
                return false;
            }

            parsed[key] = list;
        }

        verify = parsed;
        return true;
    }
}
