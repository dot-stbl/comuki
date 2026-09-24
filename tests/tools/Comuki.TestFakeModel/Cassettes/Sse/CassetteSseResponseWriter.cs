using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Sse;

/// <summary>
/// Replays a recorded SSE event sequence verbatim — reconstructs each
/// frame's original wire bytes from its <see cref="CassetteSseEvent"/>
/// (<c>event:</c> line only when <see cref="CassetteSseEvent.Type"/> was
/// captured, the literal <c>data: [DONE]</c> for the sentinel), so a
/// replayed stream is byte-identical to what <c>Recording.CassetteRecordingState</c>
/// captured — no re-derivation from a fakeScript, unlike fake mode's
/// <c>Anthropic.Response.AnthropicSseWriter</c>/<c>OpenAi.Response.OpenAiSseWriter</c>.
/// </summary>
public static class CassetteSseResponseWriter
{
    /// <summary>Writes every event in <paramref name="events"/> to <paramref name="response"/>, flushing after each one.</summary>
    public static async Task WriteAsync(HttpResponse response, IReadOnlyList<CassetteSseEvent> events, CancellationToken cancellationToken)
    {
        foreach (var sseEvent in events)
        {
            if (sseEvent.IsDone)
            {
                await response.WriteAsync($"data: {CassetteSseEvent.DoneMarker}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                continue;
            }

            if (sseEvent.Type is { } type)
            {
                await response.WriteAsync($"event: {type}\n", cancellationToken);
            }

            await response.WriteAsync($"data: {sseEvent.Data.GetRawText()}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
}
