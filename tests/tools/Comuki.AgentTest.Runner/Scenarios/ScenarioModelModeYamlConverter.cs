using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// Reads/writes <see cref="ScenarioModelMode"/> as the lowercase scalar the
/// scenario YAML shape uses (<c>fake</c>/<c>replay</c>/<c>live</c>) instead
/// of YamlDotNet's default enum-member-name matching, so the loader owns
/// one explicit, testable mapping rather than relying on a library default.
/// </summary>
public sealed class ScenarioModelModeYamlConverter : IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(ScenarioModelMode);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        return scalar.Value.Trim().ToLowerInvariant() switch
        {
            "fake" => ScenarioModelMode.Fake,
            "replay" => ScenarioModelMode.Replay,
            "live" => ScenarioModelMode.Live,
            _ => throw new ScenarioValidationException(
                $"model.mode '{scalar.Value}' is not one of fake|replay|live"),
        };
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var mode = (ScenarioModelMode)(value ?? ScenarioModelMode.Fake);
        emitter.Emit(new Scalar(mode.ToString().ToLowerInvariant()));
    }
}
