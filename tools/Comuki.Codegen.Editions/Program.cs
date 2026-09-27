using Comuki.Shared.Editions.Catalog;

namespace Comuki.Codegen.Editions;

/// <summary>
/// CLI wrapper around <see cref="RegistryEmitter"/>. Renders the
/// editions code-generated artefacts:
/// <list type="bullet">
///   <item><c>registry.ts</c> — the typed <c>Features</c>/<c>Limits</c>
///     module the dashboard imports.</item>
///   <item><c>capability-matrix.md</c> — the operator-facing capability
///     table.</item>
/// </list>
/// <c>--out &lt;path&gt;</c> writes both artefacts flat into the
/// directory; without it, the TypeScript module is written to stdout
/// (the markdown artefact is omitted because dumping two artefacts to
/// one stream has no canonical shape). All written files use
/// <c>\n</c> line endings so the artefact is byte-identical on
/// Windows and Linux CI; stdout is left raw because shells and pipes
/// decide their own line endings.
/// </summary>
public static class Program
{
    /// <summary>Entrypoint: <c>--out &lt;path&gt;</c> writes both artefacts into a directory, no flag writes the TS to stdout.</summary>
    /// <param name="args">Command-line arguments. Recognised: <c>--out &lt;path&gt;</c>.</param>
    /// <returns>0 on success, non-zero on failure (a diagnostic is written to stderr in that case).</returns>
    public static int Main(string[] args)
    {
        try
        {
            var registryTs = RegistryEmitter.EmitRegistryTs();
            var capabilityMatrix = RegistryEmitter.EmitCapabilityMatrix();

            if (ProgramHelpers.ParseOutPath(args) is { } outPath)
            {
                ProgramHelpers.WriteNormalizedFile(Path.Combine(outPath, "registry.ts"), registryTs);
                ProgramHelpers.WriteNormalizedFile(Path.Combine(outPath, "capability-matrix.md"), capabilityMatrix);
            }
            else
            {
                Console.Write(registryTs);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[Comuki.Codegen.Editions] {exception.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Pure-function helpers for <see cref="Program"/>. Lives in a
/// <c>file static class</c> so the entrypoint stays free of
/// <c>private</c> methods (project rule
/// <c>class-layout-and-tooling.md</c> §1a). Each helper is independently
/// testable and the file is the smallest possible unit that holds the
/// full CLI surface.
/// </summary>
file static class ProgramHelpers
{
    /// <summary>Extracts the <c>--out &lt;path&gt;</c> argument, or <c>null</c> when absent.</summary>
    /// <param name="args">The raw command-line arguments to scan.</param>
    public static string? ParseOutPath(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--out" && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> with all line endings normalized to <c>\n</c>.</summary>
    /// <param name="path">Destination file path. Parent directories are created when missing.</param>
    /// <param name="content">The text to write. Embedded <c>\r\n</c> sequences are rewritten to <c>\n</c> so the artefact is byte-identical across platforms.</param>
    public static void WriteNormalizedFile(string path, string content)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var normalized = content.Replace("\r\n", "\n");
        File.WriteAllText(path, normalized);
    }
}
