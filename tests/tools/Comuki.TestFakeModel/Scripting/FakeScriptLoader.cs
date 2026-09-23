using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Comuki.TestFakeModel.Scripting;

/// <summary>
/// Loads a <see cref="FakeScript"/> from disk — JSON or YAML, dispatched
/// by extension (design.md WS4 4.1: "load from JSON/YAML file or in-code
/// builder"; see <see cref="FakeScriptBuilder"/> for the in-code path).
/// </summary>
public static class FakeScriptLoader
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IDeserializer yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads <paramref name="path"/> — <c>.yaml</c>/<c>.yml</c> is parsed
    /// as YAML, everything else as JSON. The scenario name is
    /// <paramref name="scenarioNameOverride"/> when given, else the
    /// script's own <c>scenarioName</c> field, else the file's base name.
    /// </summary>
    public static FakeScript LoadFromFile(string path, string? scenarioNameOverride = null)
    {
        var text = File.ReadAllText(path);
        var extension = Path.GetExtension(path);
        var dto = IsYamlExtension(extension) ? DeserializeYaml(text) : DeserializeJson(text);
        var scenarioName = scenarioNameOverride ?? dto.ScenarioName ?? Path.GetFileNameWithoutExtension(path);
        return FakeScriptMapper.ToDomain(scenarioName, dto);
    }

    /// <summary>Loads a fakeScript from a JSON string (design.md's fakeScript file shape).</summary>
    public static FakeScript LoadFromJson(string json, string? scenarioNameOverride = null)
    {
        var dto = DeserializeJson(json);
        return FakeScriptMapper.ToDomain(scenarioNameOverride ?? dto.ScenarioName ?? "fake", dto);
    }

    /// <summary>Loads a fakeScript from a YAML string.</summary>
    public static FakeScript LoadFromYaml(string yaml, string? scenarioNameOverride = null)
    {
        var dto = DeserializeYaml(yaml);
        return FakeScriptMapper.ToDomain(scenarioNameOverride ?? dto.ScenarioName ?? "fake", dto);
    }

    private static bool IsYamlExtension(string extension)
    {
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static FakeScriptFileDto DeserializeJson(string json)
    {
        return JsonSerializer.Deserialize<FakeScriptFileDto>(json, jsonOptions)
            ?? throw new FakeScriptException("fakeScript JSON deserialized to null.");
    }

    private static FakeScriptFileDto DeserializeYaml(string yaml)
    {
        return yamlDeserializer.Deserialize<FakeScriptFileDto>(yaml)
            ?? throw new FakeScriptException("fakeScript YAML deserialized to null.");
    }
}
