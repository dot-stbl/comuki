using System.Text.Json;
using Comuki.TestFakeModel.Scripting.Dto;
using Comuki.TestFakeModel.Scripting.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Comuki.TestFakeModel.Scripting.Loading;

/// <summary>
/// Loads a <see cref="FakeScript"/> from disk — JSON or YAML, dispatched
/// by extension (design.md WS4 4.1: "load from JSON/YAML file or in-code
/// builder"; see <c>FakeScriptBuilder</c> for the in-code path).
/// </summary>
public static class FakeScriptLoader
{
    /// <summary>
    /// Loads <paramref name="path"/> — <c>.yaml</c>/<c>.yml</c> is parsed
    /// as YAML, everything else as JSON. The scenario name is
    /// <paramref name="scenarioNameOverride"/> when given, else the
    /// script's own <c>scenarioName</c> field, else the file's base name.
    /// </summary>
    public static FakeScript LoadFromFile(string path, string? scenarioNameOverride = null)
    {
        var dto = FakeScriptDeserializer.IsYaml(Path.GetExtension(path))
            ? FakeScriptDeserializer.FromYaml(File.ReadAllText(path))
            : FakeScriptDeserializer.FromJson(File.ReadAllText(path));

        return FakeScriptMapper.ToDomain(scenarioNameOverride ?? dto.ScenarioName ?? Path.GetFileNameWithoutExtension(path), dto);
    }

    /// <summary>Loads a fakeScript from a JSON string (design.md's fakeScript file shape).</summary>
    public static FakeScript LoadFromJson(string json, string? scenarioNameOverride = null)
    {
        var dto = FakeScriptDeserializer.FromJson(json);
        return FakeScriptMapper.ToDomain(scenarioNameOverride ?? dto.ScenarioName ?? "fake", dto);
    }

    /// <summary>Loads a fakeScript from a YAML string.</summary>
    public static FakeScript LoadFromYaml(string yaml, string? scenarioNameOverride = null)
    {
        var dto = FakeScriptDeserializer.FromYaml(yaml);
        return FakeScriptMapper.ToDomain(scenarioNameOverride ?? dto.ScenarioName ?? "fake", dto);
    }
}

/// <summary>The format-dispatch + deserialization steps <see cref="FakeScriptLoader"/> composes.</summary>
file static class FakeScriptDeserializer
{
    private static readonly IDeserializer yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static bool IsYaml(string extension)
    {
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    public static FakeScriptFileDto FromJson(string json)
    {
        return JsonSerializer.Deserialize<FakeScriptFileDto>(json, JsonSerializerOptions.Web)
            ?? throw new FakeScriptException("fakeScript JSON deserialized to null.");
    }

    public static FakeScriptFileDto FromYaml(string yaml)
    {
        return yamlDeserializer.Deserialize<FakeScriptFileDto>(yaml)
            ?? throw new FakeScriptException("fakeScript YAML deserialized to null.");
    }
}
