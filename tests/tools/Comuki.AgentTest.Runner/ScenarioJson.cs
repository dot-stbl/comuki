using System.Text.Json;

namespace Comuki.AgentTest.Runner;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> shape every artifact this
/// runner writes uses — <see cref="Reporting.ReportWriter"/>'s report JSON
/// and <see cref="Execution.Support.TimelineArtifactWriter"/>'s timeline
/// dump both need the same web-casing, indented output, so they share this
/// one instance instead of declaring their own.
/// </summary>
internal static class ScenarioJson
{
    /// <summary>Web-cased, indented — readable by both an agent and a human opening the artifact directly.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
    };
}
