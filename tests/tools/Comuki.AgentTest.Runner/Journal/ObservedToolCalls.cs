using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.AgentTest.Runner.Journal;

/// <summary>
/// Extracts the distinct tool names pi/TestFakePi actually invoked from a
/// run's real timeline — the <c>tool</c> field of every
/// <see cref="RunEventTypes.WorkerReported"/> entry whose payload is a
/// <c>StageActivity</c> tool-call record (<c>Comuki.Host.Workers.Grpc.WorkerStreamJournal</c>
/// serializes <c>StageActivity</c> with <c>JsonSerializerOptions.Web</c>, so
/// the field is camelCase <c>"tool"</c> on the wire). WS7 task 7.2: the
/// trajectory/forbidden-tool assertions read this set rather than the naive
/// substring match <c>TranslatorE2EShould</c> uses for its own narrower,
/// hand-picked journal checks.
/// </summary>
public static class ObservedToolCalls
{
    /// <summary>The distinct tool names observed on <paramref name="timeline"/>, in no particular order.</summary>
    public static IReadOnlySet<string> Extract(IReadOnlyList<RunEventEntry> timeline)
    {
        var tools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in timeline)
        {
            if (entry.Type == RunEventTypes.WorkerReported && TryReadToolName(entry.PayloadJson, out var tool))
            {
                tools.Add(tool);
            }
        }

        return tools;
    }

    private static bool TryReadToolName(string payloadJson, out string tool)
    {
        tool = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("tool", out var toolElement)
                && toolElement.ValueKind == JsonValueKind.String
                && toolElement.GetString() is { Length: > 0 } value)
            {
                tool = value;
                return true;
            }
        }
        catch (JsonException)
        {
            // Not every worker.reported entry is a StageActivity tool-call
            // record (Start/Report payloads parse fine but carry no "tool"
            // field) — a genuinely malformed payload is not this method's
            // problem to raise; the caller only cares about tool names.
        }

        return false;
    }
}
