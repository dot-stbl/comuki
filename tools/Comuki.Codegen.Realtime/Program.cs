using Comuki.Shared.Contracts.Realtime;

namespace Comuki.Codegen.Realtime;

/// <summary>
/// CLI wrapper around <see cref="RealtimeContractEmitter"/>. Renders the
/// realtime TypeScript contract module — one <c>export interface</c> per
/// <c>[RealtimeContract]</c> record plus the <c>RealtimeTransportMethods</c>
/// const block — either to stdout or to a file path. The CLI writes a
/// normalized file (all line endings <c>\n</c>) so the artifact is
/// byte-identical on Windows and Linux CI; stdout is left raw because
/// shells and pipes decide their own line endings.
/// </summary>
public static class Program
{
    /// <summary>Entrypoint: <c>--out &lt;path&gt;</c> writes to a file, no flag writes to stdout.</summary>
    /// <param name="args">Command-line arguments. Recognized: <c>--out &lt;path&gt;</c>.</param>
    /// <returns>0 on success, non-zero on failure (a diagnostic is written to stderr in that case).</returns>
    public static int Main(string[] args)
    {
        try
        {
            var module = RealtimeContractEmitter.Emit();
            if (ProgramHelpers.ParseOutPath(args) is { } outPath)
            {
                ProgramHelpers.WriteNormalizedFile(outPath, module);
            }
            else
            {
                Console.Write(module);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[Comuki.Codegen.Realtime] {exception.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Pure-function helpers for <see cref="Program"/>. Lives in a
/// <c>file static class</c> so the entrypoint stays free of <c>private</c>
/// methods (project rule <c>code-shape.md</c> §9). Each helper is
/// independently testable and the file is the smallest possible unit
/// that holds the full CLI surface.
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
    /// <param name="content">The text to write. Embedded <c>\r\n</c> sequences are rewritten to <c>\n</c> so the artifact is byte-identical across platforms.</param>
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
