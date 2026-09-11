using System.Globalization;
using System.Text.Json;

namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// The one flat-text projection of a part list. It is what the
/// <c>content</c> column keeps: the memory digest feeds the brain with
/// these strings, search reads them, and a client that predates parts
/// degrades to something readable rather than to nothing. Markdown, so a
/// renderer that knows nothing about parts still shows fenced code and a
/// plan payload.
/// </summary>
public static class MessagePartText
{
    /// <summary>Blank line between blocks — one part, one block.</summary>
    public const string BlockSeparator = "\n\n";

    /// <summary>Marker appended when the projection is clamped to a length bound.</summary>
    public const string TruncationMarker = "…";

    /// <summary>Projects an ordered part list onto one markdown string.</summary>
    /// <param name="parts">Ordered parts of one message.</param>
    public static string Flatten(IReadOnlyList<MessagePart> parts)
    {
        var blocks = parts
            .Select(static part => MessagePartBlocks.Of(part))
            .Where(static block => block.Length > 0)
            .ToArray();

        return string.Join(BlockSeparator, blocks);
    }

    /// <summary>
    /// Clamps a projection to a storage bound, marking the cut. The
    /// transcript column is bounded, parts are not — a long tool
    /// observation must shorten the projection, never fail the turn.
    /// </summary>
    /// <param name="text">Flattened projection.</param>
    /// <param name="maxLength">Upper bound, in characters.</param>
    public static string Clamp(string text, int maxLength)
    {
        return text.Length <= maxLength
            ? text
            : string.Concat(text.AsSpan(0, maxLength - TruncationMarker.Length), TruncationMarker);
    }
}

/// <summary>One part → one markdown block.</summary>
file static class MessagePartBlocks
{
    /// <summary>Renders a single part; an empty result drops the block.</summary>
    /// <param name="part">The part to render.</param>
    public static string Of(MessagePart part)
    {
        return part switch
        {
            MessagePart.TextPart text => text.Markdown,
            MessagePart.CodePart code => Anchored(code),
            MessagePart.DiagramPart diagram => Fence(diagram.Dialect, diagram.Source),
            MessagePart.ThinkingPart thinking => "_thinking_\n" + thinking.Text,
            MessagePart.ToolPart tool => Tool(tool),
            MessagePart.HandoffPart handoff => "handoff: " + handoff.Query,
            MessagePart.PlanPart plan => Fence("json", JsonSerializer.Serialize(plan, JsonSerializerOptions.Web)),
            _ => string.Empty,
        };
    }

    /// <summary>Code listing, preceded by its repository anchor when it has one.</summary>
    /// <param name="code">The code part.</param>
    public static string Anchored(MessagePart.CodePart code)
    {
        var fence = Fence(code.Language, code.Source);
        return code.Path is { Length: > 0 } path
            ? "`" + path + Line(code.StartLine) + "`\n" + fence
            : fence;
    }

    /// <summary>Tool call: the name and status on one line, the payload fenced under it.</summary>
    /// <param name="tool">The tool part.</param>
    public static string Tool(MessagePart.ToolPart tool)
    {
        var payload = tool.OutputJson is { Length: > 0 } output ? output : tool.InputJson;
        return "tool `" + tool.Name + "` — " + tool.Status + "\n" + Fence("json", payload);
    }

    /// <summary>Fenced block in the given language.</summary>
    /// <param name="language">Fence info string.</param>
    /// <param name="source">Fenced body.</param>
    public static string Fence(string language, string source)
    {
        return "```" + language + "\n" + source + "\n```";
    }

    /// <summary>Line suffix of a code anchor; empty when the snippet is unanchored.</summary>
    /// <param name="startLine">1-based start line, if known.</param>
    public static string Line(int? startLine)
    {
        return startLine is { } line ? ":" + line.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
