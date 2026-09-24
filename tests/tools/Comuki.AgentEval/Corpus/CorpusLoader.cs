using Comuki.AgentTest.Runner.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Comuki.AgentEval.Corpus;

/// <summary>
/// Loads the WS10 corpus: every <c>*.scenario.yaml</c>/<c>*.scenario.json</c>
/// under a directory tree (skipping <c>scripts/</c>, <c>cassettes/</c>,
/// <c>anonymized-real/</c> subfolders — those carry supporting files, not
/// scenario entries), wrapped together with the entry's companion
/// <see cref="EvalExtension"/> parsed from its top-level <c>eval:</c> key.
/// </summary>
/// <remarks>
/// <para>
/// The base <see cref="ScenarioDefinition"/> is loaded by the EXISTING
/// <c>Comuki.AgentTest.Runner.Scenarios.ScenarioLoader.Load</c>, which is
/// reused unmodified. Its <c>IgnoreUnmatchedProperties()</c> setting means
/// a scenario file's extra <c>eval:</c> top-level key never breaks the base
/// load — exactly the design rationale the loader was set up with.
/// </para>
/// <para>
/// The <c>eval:</c> block is parsed separately by this loader through its
/// own small YamlDotNet deserializer against a thin wrapper type. That
/// wrapper has <c>Eval</c> as its only field; the deserializer ignores
/// every other top-level property (the entire base scenario shape), so a
/// single <c>YamlDotNet</c> deserializer call per file reads the
/// <c>eval:</c> block without dragging the base scenario shape into a
/// second schema.
/// </para>
/// <para>
/// Subfolders <c>scripts/</c>, <c>cassettes/</c>, and
/// <c>anonymized-real/</c> are skipped during the scenario-file glob —
/// those directories exist alongside <c>*.scenario.yaml</c> files to hold
/// supporting assets (fakeScripts, replay cassettes, a future
/// human-reviewed anonymized corpus slice). The <c>anonymized-real/</c>
/// folder is the documented slot from design.md "Anonymized real tickets"
/// (open question) — intentionally inert until a human review passes its
/// content; this loader is the place that enforces "this folder is inert
/// even when someone drops a stray <c>.scenario.yaml</c> into it".
/// </para>
/// </remarks>
public static class CorpusLoader
{
    private static readonly IDeserializer evalDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Recursively globs <paramref name="corpusDirectory"/> for
    /// <c>*.scenario.yaml</c> / <c>*.scenario.json</c> files (skipping the
    /// supporting-asset subfolders named above), loads each through the base
    /// <c>ScenarioLoader</c>, parses its companion <c>eval:</c> block, and
    /// returns the resulting list.
    /// </summary>
    /// <param name="corpusDirectory">Absolute or repo-relative path to the corpus root directory.</param>
    /// <param name="filterName">Optional exact-name match against <see cref="ScenarioDefinition.Name"/>; when non-null, only matching entries are returned.</param>
    /// <exception cref="CorpusValidationException">The directory is absent, a scenario file fails to parse, or a base scenario fails structural validation. Missing <c>eval:</c> blocks are not errors.</exception>
    public static IReadOnlyList<CorpusEntry> LoadDirectory(string corpusDirectory, string? filterName = null)
    {
        var absoluteDirectory = Path.GetFullPath(corpusDirectory);
        if (!Directory.Exists(absoluteDirectory))
        {
            throw new CorpusValidationException($"corpus directory not found: {absoluteDirectory}");
        }

        var entries = new List<CorpusEntry>();
        foreach (var scenarioPath in EnumerateScenarioFiles(absoluteDirectory))
        {
            CorpusEntry entry;
            try
            {
                var scenario = ScenarioLoader.Load(scenarioPath);
                var eval = ParseEvalBlock(scenarioPath);
                entry = new CorpusEntry(scenario, eval, scenarioPath);
            }
            catch (ScenarioValidationException exception)
            {
                throw new CorpusValidationException(
                    $"corpus entry '{scenarioPath}' failed base scenario validation: {exception.Message}",
                    exception);
            }

            if (filterName is not null && entry.Scenario.Name != filterName)
            {
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static IEnumerable<string> EnumerateScenarioFiles(string corpusDirectory)
    {
        foreach (var path in Directory.EnumerateFiles(corpusDirectory, "*.scenario.yaml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(corpusDirectory, "*.scenario.json", SearchOption.AllDirectories)))
        {
            // Skip supporting-asset subfolders — they hold fakeScripts,
            // cassettes, and the anonymized-real slot (inert by design),
            // never scenario files.
            if (IsUnderSkippedAssetFolder(corpusDirectory, path))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsUnderSkippedAssetFolder(string corpusRoot, string filePath)
    {
        var relative = Path.GetRelativePath(corpusRoot, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment => segment is "scripts" or "cassettes" or "anonymized-real");
    }

    private static EvalExtension ParseEvalBlock(string scenarioPath)
    {
        var wrapper = evalDeserializer.Deserialize<EvalYamlDocument>(File.ReadAllText(scenarioPath));
        return wrapper?.Eval ?? new EvalExtension();
    }

    /// <summary>
    /// Thin wrapper for the deserializer. Only the <see cref="Eval"/>
    /// property is modeled; YamlDotNet's <c>IgnoreUnmatchedProperties()</c>
    /// discards every other top-level property (the entire base scenario
    /// shape) so a single YamlDotNet call reads just the eval block.
    /// </summary>
    /// <remarks>
    /// Init-only properties (no positional primary constructor) so
    /// YamlDotNet's <c>DefaultObjectFactory</c> can materialize the
    /// wrapper via its parameterless constructor — records with a
    /// positional primary constructor trigger
    /// <c>MissingMethodException</c> on deserialization.
    /// </remarks>
    private sealed class EvalYamlDocument
    {
        public EvalExtension? Eval { get; init; }
    }
}
