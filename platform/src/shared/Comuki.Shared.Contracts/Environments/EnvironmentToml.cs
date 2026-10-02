using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Model;

namespace Comuki.Shared.Contracts.Environments;

/// <summary>
/// Strict parser for <c>.comuki/environment.toml</c> (add-worker-environments
/// 4.1). Closed schema: top-level keys are exactly <c>schema</c>, <c>class</c>,
/// <c>runtime</c>, <c>restore</c>, <c>mounts</c> — anything else is a parse
/// failure. Anything else would let a hostile repo smuggle a
/// <c>postCreate</c> shell command into the worker pool; the spec
/// "Closed schema rejects shell" scenario is enforced here. Field-level
/// walking lives in <see cref="EnvironmentTomlReader"/>; this type owns the
/// orchestration and the class-aware opcode validation.
/// </summary>
public static class EnvironmentToml
{
    /// <summary>Schema version this parser implements.</summary>
    public const int SupportedSchema = 1;

    /// <summary>
    /// Parse the supplied TOML content into an <see cref="EnvironmentTomlFile" />.
    /// On any failure returns false and populates <paramref name="errors" />;
    /// <paramref name="file" /> is null. Errors are deterministic and
    /// machine-readable: one entry per problem, no aggregation.
    /// </summary>
    /// <param name="content">Raw toml text. Treated as TOML — JSON/YAML
    ///     fails the Tomlyn parse and surfaces here, not via a typed
    ///     exception (the spec mandates "JSON and YAML SHALL NOT be
    ///     accepted at this path").</param>
    /// <param name="file">Parsed projection when the call succeeds, else null.</param>
    /// <param name="errors">Empty on success; one entry per failure on error.</param>
    /// <returns>True iff the content parses into a valid environment file.</returns>
    public static bool TryParse(string content, [NotNullWhen(true)] out EnvironmentTomlFile? file, out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
        {
            file = null;
            errors = ["environment.toml is empty."];
            return false;
        }

        TomlTable root;
        try
        {
            // Same path Comuki.Shared.Bootstrap's config.toml pipeline uses:
            // deserialize into the loose model, then walk it. Tomlyn throws
            // TomlException on syntactic TOML failures — which is what makes
            // JSON / YAML get refused at this path naturally.
            var model = TomlSerializer.Deserialize(content, typeof(TomlTable), TomlSerializerOptions.Default);
            // boundary: TomlSerializer.Deserialize is non-null on a valid TOML payload
            root = (TomlTable)model!;
        }
        catch (TomlException exception)
        {
            file = null;
            errors = [$"environment.toml is not valid TOML: {exception.Message}"];
            return false;
        }

        foreach (var key in root.Keys)
        {
            if (!EnvironmentTomlReader.AllowedTopLevelKeys.Contains(key))
            {
                collected.Add($"environment.toml has unknown top-level key '{key}'.");
            }
        }

        if (!EnvironmentTomlReader.TryReadSchema(root, collected, out _)
            || !EnvironmentTomlReader.TryReadClass(root, collected, out var classId)
            || !EnvironmentTomlReader.TryReadRuntime(root, collected, out var runtime)
            || !EnvironmentTomlReader.TryReadRestore(root, collected, out var restore)
            || !EnvironmentTomlReader.TryReadMounts(root, collected, out var mounts)
            || !EnvironmentTomlReader.TryReadVerify(root, collected, out var verify)
            || collected.Count > 0)
        {
            file = null;
            errors = collected;
            return false;
        }

        file = new EnvironmentTomlFile(classId, runtime, restore, mounts, verify);
        errors = [];
        return true;
    }

    /// <summary>
    /// Validate the parsed <see cref="EnvironmentTomlFile.Restore" /> keys
    /// against the opcodes the bound class advertises. The parser does not
    /// know the catalog — the class-aware caller passes the opcode set, so
    /// the Engine.Compute catalog remains the single source of truth and
    /// this parser stays catalog-agnostic.
    /// </summary>
    /// <param name="file">A previously-parsed environment file.</param>
    /// <param name="allowedOpcodes">Opcodes the bound class permits; every key
    ///     in <see cref="EnvironmentTomlFile.Restore" /> must be a member.</param>
    /// <param name="errors">Empty on success; one entry per disallowed key.</param>
    /// <returns>True iff every restore key is in <paramref name="allowedOpcodes" />.</returns>
    public static bool ValidateRestoreOpcodes(
        EnvironmentTomlFile file,
        IReadOnlySet<string> allowedOpcodes,
        out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();
        foreach (var key in file.Restore.Keys)
        {
            if (!allowedOpcodes.Contains(key))
            {
                collected.Add($"environment.toml restore opcode '{key}' is not advertised by the bound class.");
            }
        }

        errors = collected;
        return collected.Count == 0;
    }
}
