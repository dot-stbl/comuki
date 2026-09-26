using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Catalog;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Drift detector for the two code-generated artefacts the editions
/// catalogue owns (issue #164 E3, change <c>add-editions-and-licensing</c>
/// WS2):
/// <list type="bullet">
///   <item><c>dashboard/src/shared/editions/_generated/registry.ts</c></item>
///   <item><c>openspec/specs/editions/capability-matrix.md</c></item>
/// </list>
/// The committed artefacts must match a fresh emission byte-for-byte.
/// A casual key reshuffle, a rank→code map edit, or a missing feature
/// row fails this test before the diff reaches review.
/// <para>
/// The walk-up from <see cref="AppContext.BaseDirectory"/> finds the
/// solution root by climbing until a directory containing
/// <c>comuki.slnx</c> is found. From there the two artefacts are read
/// off disk and compared to what <see cref="RegistryEmitter.EmitRegistryTs"/>
/// and <see cref="RegistryEmitter.EmitCapabilityMatrix"/> produce. The
/// emitter reaches the same <see cref="Features"/>/<see cref="Limits"/>
/// state every assembly load reads, so this is a pure byte compare.
/// </para>
/// </summary>
public sealed class GeneratedEditionsArtifactsMatchRegistryShould
{
    private const string SolutionFileName = "comuki.slnx";
    private const string RegistryTsRelativePath = "dashboard/src/shared/editions/_generated/registry.ts";
    private const string CapabilityMatrixRelativePath = "openspec/specs/editions/capability-matrix.md";

    [Fact(DisplayName = "Given the committed dashboard registry.ts, when the emitter re-renders, then the bytes match exactly")]
    public void CommittedRegistryTsMatchesEmitted()
    {
        var committedPath = ResolveRepoFile(RegistryTsRelativePath);
        var committed = File.ReadAllText(committedPath);
        var emitted = NormalizeLineEndings(RegistryEmitter.EmitRegistryTs());

        emitted.ShouldBe(
            committed,
            $"Registry artifact drifted from the catalogue: {committedPath}. Regenerate via `dotnet run --project tools/Comuki.Codegen.Editions` (or rebuild Debug).");
    }

    [Fact(DisplayName = "Given the committed capability matrix, when the emitter re-renders, then the bytes match exactly")]
    public void CommittedCapabilityMatrixMatchesEmitted()
    {
        var committedPath = ResolveRepoFile(CapabilityMatrixRelativePath);
        var committed = File.ReadAllText(committedPath);
        var emitted = NormalizeLineEndings(RegistryEmitter.EmitCapabilityMatrix());

        emitted.ShouldBe(
            committed,
            $"Capability matrix drifted from the catalogue: {committedPath}. Regenerate via `dotnet run --project tools/Comuki.Codegen.Editions` (or rebuild Debug).");
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until the
    /// repository root (the directory containing
    /// <c>comuki.slnx</c>) is found, then returns the absolute path of
    /// <paramref name="relativePath"/> beneath it. Throws when the
    /// solution file is not found within ten hops — the test project
    /// must live somewhere below the repo root for this to work.
    /// </summary>
    /// <param name="relativePath">Path relative to the repository root.</param>
    private static string ResolveRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var hop = 0; hop < 10 && directory is not null; hop++)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFileName} within 10 parent directories of {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Rewrites every <c>\r\n</c> to <c>\n</c> so the committed artefact
    /// (which may have been normalised through git or a Windows editor)
    /// compares equal to what the emitter produces. <see cref="string.Replace(string, string)"/>
    /// is fine here: the pipe / newline characters involved have no
    /// culture-sensitive casing.
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n");
    }
}
