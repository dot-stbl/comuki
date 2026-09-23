namespace Comuki.TestFakeModel.Unit;

/// <summary>One parsed SSE event: the <c>event:</c> line's value and the raw <c>data:</c> line's value.</summary>
internal sealed record SseEvent(string EventType, string Data);

/// <summary>Parses a raw SSE response body into <see cref="SseEvent"/>s for framing assertions.</summary>
internal static class SseTestHelpers
{
    /// <summary>
    /// Splits <paramref name="rawBody"/> on blank lines (the SSE event
    /// separator) and reads each block's <c>event:</c>/<c>data:</c> line
    /// pair, in order.
    /// </summary>
    public static IReadOnlyList<SseEvent> Parse(string rawBody)
    {
        var events = new List<SseEvent>();
        var blocks = rawBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            string? eventType = null;
            string? data = null;
            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventType = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
            }

            if (eventType is not null && data is not null)
            {
                events.Add(new SseEvent(eventType, data));
            }
        }

        return events;
    }
}
