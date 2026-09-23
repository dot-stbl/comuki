using System.Text.Json;

namespace Comuki.TestFakeModel.Cassettes.IO;

/// <summary>Loads a <see cref="CassetteFile"/> from disk or a JSON string — JSON-only (no YAML: cassettes are written by this tool, never hand-authored).</summary>
public static class CassetteReader
{
    /// <summary>Loads the cassette at <paramref name="path"/>.</summary>
    public static CassetteFile LoadFromFile(string path)
    {
        return LoadFromJson(File.ReadAllText(path));
    }

    /// <summary>Loads a cassette from a JSON string.</summary>
    public static CassetteFile LoadFromJson(string json)
    {
        var cassette = JsonSerializer.Deserialize<CassetteFile>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("cassette JSON deserialized to null.");

        return cassette.SchemaVersion == CassetteFile.CurrentSchemaVersion
            ? cassette
            : throw new InvalidOperationException(
                $"cassette '{cassette.Scenario}' has schemaVersion {cassette.SchemaVersion}, this build only reads {CassetteFile.CurrentSchemaVersion}.");
    }
}
