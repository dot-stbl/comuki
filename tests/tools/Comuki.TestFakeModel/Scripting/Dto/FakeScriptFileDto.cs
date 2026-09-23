namespace Comuki.TestFakeModel.Scripting.Dto;

/// <summary>
/// On-disk shape of a fakeScript file — deserialized from JSON
/// (System.Text.Json) or YAML (YamlDotNet with the camelCase naming
/// convention) into the exact same DTO tree, then mapped onto the
/// <c>FakeScript</c> domain model. Every field is loosely typed on
/// purpose (settable properties, no required-ness) — both deserializers
/// populate by property name and neither tolerates constructor binding
/// cleanly.
/// </summary>
public sealed record FakeScriptFileDto
{
    /// <summary>Overrides the id-generation scenario name; falls back to the script file's base name when omitted.</summary>
    public string? ScenarioName { get; set; }

    /// <summary>The ordered scripted entries.</summary>
    public List<FakeScriptEntryDto> Entries { get; set; } = [];
}
