using System.Text.Json;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Execution.Support;

/// <summary>Dumps a run's journal timeline to disk as evidence — the artifact a failure report's <c>artifactPaths</c> points at.</summary>
public static class TimelineArtifactWriter
{

    /// <summary>
    /// Writes <paramref name="timeline"/> to <c>&lt;directory&gt;/&lt;scenarioName&gt;.timeline.json</c>
    /// and returns the written path (relative to <paramref name="directory"/>, for a report's <c>artifactPaths</c>).
    /// </summary>
    /// <param name="directory">Destination directory — created if missing.</param>
    /// <param name="scenarioName">The scenario's name — becomes the file's stem.</param>
    /// <param name="timeline">The run's timeline, oldest first.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<string> WriteAsync(
        string directory,
        string scenarioName,
        IReadOnlyList<RunEventEntry> timeline,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var fileName = scenarioName + ".timeline.json";
        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(
                timeline.Select(static entry => new
                {
                    entry.Id,
                    RunId = entry.RunId.Value,
                    entry.Type,
                    entry.OccurredAt,
                    Payload = JsonSerializer.Deserialize<JsonElement>(entry.PayloadJson),
                }),
                ScenarioJson.Options),
            cancellationToken);
        return fileName;
    }
}
