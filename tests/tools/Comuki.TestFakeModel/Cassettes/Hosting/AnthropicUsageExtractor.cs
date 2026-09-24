using System.Text.Json;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>
/// Pure, dependency-free extraction of Anthropic-style token-usage
/// numbers out of a <see cref="CassetteResponse"/> that was recorded by
/// <see cref="Recording.CassetteUpstreamForwarder"/>. Used by the live-mode
/// harness (<c>RealPi.LiveModePiFakeModelHarness</c>) to compute the cost
/// the budget-cap helper tracks.
/// </summary>
/// <remarks>
/// <para>
/// Anthropic's two wire shapes carry usage differently (per
/// <c>Anthropic.Wire.AnthropicUsage</c>):
/// <list type="bullet">
///   <item>
///     <c>stream: false</c> — the response root has a <c>usage</c> object
///     with <c>input_tokens</c> and <c>output_tokens</c>.
///   </item>
///   <item>
///     <c>stream: true</c> — input tokens ride a <c>message_start</c>
///     event's <c>message.usage</c>; output tokens ride the terminal
///     <c>message_delta</c> event's <c>usage</c>.
///   </item>
/// </list>
/// This helper handles both. Missing fields collapse to zero — the
/// caller's <c>RunCost</c> has no opinion on missing token
/// counts (a zero-cost exchange is the default).
/// </para>
/// <para>
/// OpenAI usage shapes (<c>prompt_tokens</c>/<c>completion_tokens</c>)
/// are deliberately out of scope here — the live harness is the hapy
/// gateway backed by Anthropic; live-mode OpenAI is a later wire-shape
/// extension and would need its own extractor (per
/// <c>OpenAi.Wire.OpenAiChatCompletionResponse</c>'s
/// <c>usage.prompt_tokens</c>/<c>usage.completion_tokens</c>).
/// </para>
/// </remarks>
public static class AnthropicUsageExtractor
{
    /// <summary>The Anthropic usage field names — names extracted from the Anthropic docs / this project's own <c>Anthropic.Wire.AnthropicUsage</c>.</summary>
    public const string InputTokensField = "input_tokens";

    /// <summary>The Anthropic usage field names — names extracted from the Anthropic docs / this project's own <c>Anthropic.Wire.AnthropicUsage</c>.</summary>
    public const string OutputTokensField = "output_tokens";

    /// <summary>Pulls input/output token counts out of <paramref name="response"/>.</summary>
    /// <param name="response">A recorded Anthropic response — streamed or non-streamed.</param>
    public static UsageCounts Extract(CassetteResponse response)
    {
        return response.Streamed
            ? AnthropicUsageStreamedExtractor.Extract(response.Events)
            : AnthropicUsageNonStreamedExtractor.Extract(response.Body);
    }

    /// <summary>Token counts the live harness adds to its recording-side budget tracker.</summary>
    public readonly record struct UsageCounts(int InputTokens, int OutputTokens)
    {
        /// <summary>Default — zero-everything — sentinel for "this exchange did not report usage".</summary>
        public static UsageCounts Empty { get; } = new(0, 0);

        /// <summary>Sum the two sides into a single integer pair for the parent's reporting RunCost contribution.</summary>
        public int TotalTokens => InputTokens + OutputTokens;
    }
}

/// <summary>The non-streamed usage-extraction step <see cref="AnthropicUsageExtractor.Extract"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicUsageNonStreamedExtractor
{
    /// <summary>Reads <c>usage.input_tokens</c> + <c>usage.output_tokens</c> off the response body; returns <see cref="AnthropicUsageExtractor.UsageCounts.Empty"/> when the body is missing or has no usage object.</summary>
    public static AnthropicUsageExtractor.UsageCounts Extract(JsonElement? body)
    {
        if (body is not { } b || b.ValueKind != JsonValueKind.Object)
        {
            return AnthropicUsageExtractor.UsageCounts.Empty;
        }

        if (b.TryGetProperty("usage", out var usage))
        {
            if (usage.ValueKind == JsonValueKind.Object)
            {
                return new AnthropicUsageExtractor.UsageCounts(
                    AnthropicUsageStreamedExtractor.ReadInt(usage, AnthropicUsageExtractor.InputTokensField),
                    AnthropicUsageStreamedExtractor.ReadInt(usage, AnthropicUsageExtractor.OutputTokensField));
            }
        }

        return AnthropicUsageExtractor.UsageCounts.Empty;
    }
}

/// <summary>The streamed usage-extraction step <see cref="AnthropicUsageExtractor.Extract"/> composes — extracted per class-layout-and-tooling.md §1a.</summary>
file static class AnthropicUsageStreamedExtractor
{
    /// <summary>The Anthropic usage-object field name on streamed events.</summary>
    private const string UsageField = "usage";

    /// <summary>Walks <c>message_start.message.usage.input_tokens</c> + <c>message_delta.usage.output_tokens</c> off the SSE event stream; returns <see cref="AnthropicUsageExtractor.UsageCounts.Empty"/> when the stream is missing.</summary>
    public static AnthropicUsageExtractor.UsageCounts Extract(IReadOnlyList<CassetteSseEvent>? events)
    {
        if (events is null)
        {
            return AnthropicUsageExtractor.UsageCounts.Empty;
        }

        var inputTokens = 0;
        var outputTokens = 0;

        foreach (var sseEvent in events)
        {
            if (sseEvent.Data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (sseEvent.Type)
            {
                case "message_start":
                    inputTokens = ReadInt(sseEvent.Data, $"message.{UsageField}.{AnthropicUsageExtractor.InputTokensField}");
                    break;
                case "message_delta":
                    outputTokens = ReadInt(sseEvent.Data, $"{UsageField}.{AnthropicUsageExtractor.OutputTokensField}");
                    break;
            }
        }

        return new AnthropicUsageExtractor.UsageCounts(inputTokens, outputTokens);
    }

    /// <summary>Reads a single integer property at <paramref name="dottedPath"/> (e.g. <c>"usage.input_tokens"</c>) from <paramref name="parent"/>; returns 0 when any segment is missing or non-numeric.</summary>
    public static int ReadInt(JsonElement parent, string dottedPath)
    {
        var current = parent;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
            {
                return 0;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value) ? value : 0;
    }
}
