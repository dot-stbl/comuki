using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// Loads a <see cref="ScenarioDefinition"/> from a YAML file on disk
/// (design.md "Scenario format" — JSON is accepted too, same schema,
/// since YAML is a JSON superset) and validates it against the structural
/// rules the add-agentic-test-contour spec calls out explicitly (schema
/// version, replay-mode cassette presence). Field-level business rules
/// beyond that (e.g. "does the fixture repo directory exist") are the
/// runner's job, not the loader's — the loader only proves the file
/// parses into a well-formed <see cref="ScenarioDefinition"/>.
/// </summary>
public static class ScenarioLoader
{
    private static readonly IDeserializer deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ScenarioModelModeYamlConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads and validates the scenario at <paramref name="scenarioPath"/>.
    /// </summary>
    /// <param name="scenarioPath">Absolute or relative path to the <c>.scenario.yaml</c> (or <c>.json</c>) file.</param>
    /// <exception cref="ScenarioValidationException">The file is missing, does not parse, or violates a structural rule.</exception>
    public static ScenarioDefinition Load(string scenarioPath)
    {
        if (!File.Exists(scenarioPath))
        {
            throw new ScenarioValidationException($"scenario file not found: {scenarioPath}");
        }

        ScenarioDefinition scenario;
        try
        {
            using var reader = new StreamReader(scenarioPath);
            scenario = deserializer.Deserialize<ScenarioDefinition>(reader)
                ?? throw new ScenarioValidationException($"scenario file is empty: {scenarioPath}");
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            // YamlDotNet wraps a custom IYamlTypeConverter's own exception
            // (e.g. ScenarioModelModeYamlConverter's "not one of fake|replay|live")
            // as YamlException.InnerException with a generic outer message —
            // surface the innermost one too, or the specific problem is lost.
            var innermost = InnermostMessage(exception);
            throw new ScenarioValidationException(
                $"scenario file '{scenarioPath}' failed to parse: {exception.Message}"
                    + (innermost == exception.Message ? string.Empty : $" ({innermost})"),
                exception);
        }

        Validate(scenario, scenarioPath);
        return scenario;
    }

    /// <summary>
    /// Structural checks the spec requires up front, independent of any
    /// particular execution mode: schema version, non-empty name, and —
    /// per the "Mode mismatch is explicit" requirement — a <c>replay</c>
    /// model mode must reference a cassette file that exists next to the
    /// scenario, checked before any run is attempted.
    /// </summary>
    private static void Validate(ScenarioDefinition scenario, string scenarioPath)
    {
        if (scenario.SchemaVersion != 1)
        {
            throw new ScenarioValidationException(
                $"scenario '{scenarioPath}' declares schemaVersion {scenario.SchemaVersion}; this runner understands schemaVersion 1 only");
        }

        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            throw new ScenarioValidationException($"scenario '{scenarioPath}' has no name");
        }

        if (string.IsNullOrWhiteSpace(scenario.Worker.Image))
        {
            throw new ScenarioValidationException($"scenario '{scenario.Name}' has no worker.image");
        }

        if (scenario.Model is { Mode: ScenarioModelMode.Replay } model)
        {
            if (string.IsNullOrWhiteSpace(model.Cassette))
            {
                throw new ScenarioValidationException(
                    $"scenario '{scenario.Name}' declares model.mode: replay but sets no model.cassette");
            }

            var cassettePath = ResolveRelativeToScenario(scenarioPath, model.Cassette);
            if (!File.Exists(cassettePath))
            {
                throw new ScenarioValidationException(
                    $"scenario '{scenario.Name}' declares model.mode: replay against cassette '{model.Cassette}', "
                        + $"but no file exists at '{cassettePath}'");
            }
        }
    }

    /// <summary>Resolves a scenario-relative path (fixture, cassette, fake script) against the scenario file's own directory.</summary>
    /// <param name="scenarioPath">The scenario file's path.</param>
    /// <param name="relativePath">A path from one of the scenario's own fields.</param>
    public static string ResolveRelativeToScenario(string scenarioPath, string relativePath)
    {
        var scenarioDirectory = Path.GetDirectoryName(Path.GetFullPath(scenarioPath))
            ?? throw new ScenarioValidationException($"could not resolve the directory of '{scenarioPath}'");
        return Path.GetFullPath(Path.Combine(scenarioDirectory, relativePath));
    }

    private static string InnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is { } inner)
        {
            current = inner;
        }

        return current.Message;
    }
}
