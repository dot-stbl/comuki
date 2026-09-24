using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Sse;

/// <summary>
/// Parses a raw SSE response body — either wire shape (Anthropic's
/// <c>event:</c>+<c>data:</c> pairs or OpenAI's bare <c>data:</c> lines) —
/// into <see cref="CassetteSseEvent"/>s for <c>Recording.CassetteRecordingState</c>
/// to capture. Protocol-agnostic: it only understands SSE framing, not
/// either provider's JSON payload shape.
/// </summary>
public static class SseFrameParser
{
    /// <summary>Splits <paramref name="rawBody"/> on blank lines (the SSE event separator) and parses each block.</summary>
    public static IReadOnlyList<CassetteSseEvent> Parse(string rawBody)
    {
        var events = new List<CassetteSseEvent>();
        foreach (var block in rawBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (SseFrameBlock.TryParse(block, out var frameEvent))
            {
                events.Add(frameEvent);
            }
        }

        return events;
    }
}

/// <summary>The per-frame parsing step <see cref="SseFrameParser"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class SseFrameBlock
{
    public static bool TryParse(string block, [NotNullWhen(true)] out CassetteSseEvent? frameEvent)
    {
        string? type = null;
        string? data = null;
        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                type = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line["data: ".Length..];
            }
        }

        if (data is null)
        {
            frameEvent = null;
            return false;
        }

        if (data == CassetteSseEvent.DoneMarker)
        {
            frameEvent = CassetteSseEvent.Done;
            return true;
        }

        using var parsedData = JsonDocument.Parse(data);
        frameEvent = new CassetteSseEvent(type, parsedData.RootElement.Clone());
        return true;
    }
}
