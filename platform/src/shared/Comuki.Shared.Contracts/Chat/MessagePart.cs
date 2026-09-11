using System.Text.Json.Serialization;
using Comuki.Shared.Contracts.Plans;

namespace Comuki.Shared.Contracts.Chat;

/// <summary>
/// One fragment of a chat message. A message is an ordered list of parts,
/// so a single assistant turn can carry prose, three tool calls and a plan
/// card without becoming five unrelated transcript rows.
/// <para>
/// The union is closed to nested records (private constructor) and
/// discriminated on the wire by <c>kind</c> — the keys live in
/// <see cref="MessagePartKinds"/>. New kinds are added here as further
/// nested records plus a <see cref="JsonDerivedTypeAttribute"/> line; the
/// P2 question/decision parts land exactly that way.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextPart), MessagePartKinds.Text)]
[JsonDerivedType(typeof(CodePart), MessagePartKinds.Code)]
[JsonDerivedType(typeof(DiagramPart), MessagePartKinds.Diagram)]
[JsonDerivedType(typeof(ThinkingPart), MessagePartKinds.Thinking)]
[JsonDerivedType(typeof(ToolPart), MessagePartKinds.Tool)]
[JsonDerivedType(typeof(HandoffPart), MessagePartKinds.Handoff)]
[JsonDerivedType(typeof(PlanPart), MessagePartKinds.Plan)]
public abstract record MessagePart
{
    private MessagePart()
    {
    }

    /// <summary>Markdown prose — the default shape of a reply.</summary>
    /// <param name="Markdown">Message text in markdown.</param>
    public sealed record TextPart(string Markdown) : MessagePart;

    /// <summary>
    /// A source listing. <paramref name="Path"/> and
    /// <paramref name="StartLine"/> anchor it in a repository when the
    /// fragment was read from one.
    /// </summary>
    /// <param name="Language">Highlighting language key (<c>csharp</c>, <c>ts</c>, …).</param>
    /// <param name="Source">The listing itself, verbatim.</param>
    /// <param name="Path">Repository-relative file path; null when the snippet is free-standing.</param>
    /// <param name="StartLine">1-based line the listing starts at; null when unanchored.</param>
    public sealed record CodePart(
        string Language,
        string Source,
        string? Path = null,
        int? StartLine = null) : MessagePart;

    /// <summary>A diagram carried as its source text; the console renders it.</summary>
    /// <param name="Dialect">Diagram dialect (<c>mermaid</c>, <c>dot</c>, …).</param>
    /// <param name="Source">Diagram source, verbatim.</param>
    public sealed record DiagramPart(string Dialect, string Source) : MessagePart;

    /// <summary>The model's visible reasoning — collapsed by default in the console.</summary>
    /// <param name="Text">Reasoning text.</param>
    /// <param name="Tokens">Reasoning tokens the model reported; null when unknown.</param>
    public sealed record ThinkingPart(string Text, int? Tokens = null) : MessagePart;

    /// <summary>
    /// One tool invocation. A running call is journalled with
    /// <see cref="ToolPartStatuses.Running"/> and no output; the terminal
    /// form carries the observation.
    /// </summary>
    /// <param name="Name">Tool name as the runtime knows it (<c>create_ticket</c>, …).</param>
    /// <param name="InputJson">Arguments the tool was called with, as JSON.</param>
    /// <param name="Status">A value from <see cref="ToolPartStatuses"/>.</param>
    /// <param name="OutputJson">Observation payload as JSON; null while running.</param>
    /// <param name="DurationMs">Wall-clock duration in milliseconds; null while running.</param>
    public sealed record ToolPart(
        string Name,
        string InputJson,
        string Status,
        string? OutputJson = null,
        long? DurationMs = null) : MessagePart;

    /// <summary>
    /// A hand-off to a screen. The console resolves the query through the
    /// same search shapes the command palette uses, so chat and palette
    /// cannot disagree about where an answer lives.
    /// </summary>
    /// <param name="Query">Search query that opens the destination.</param>
    public sealed record HandoffPart(string Query) : MessagePart;

    /// <summary>
    /// The decomposition DAG — the approve card. Nodes and edges are the
    /// canonical <see cref="Plan"/> shapes, so a part and a plan payload
    /// never drift apart.
    /// </summary>
    /// <param name="Nodes">Plan steps.</param>
    /// <param name="Edges">Ordering dependencies between steps.</param>
    public sealed record PlanPart(
        IReadOnlyList<PlanNode> Nodes,
        IReadOnlyList<PlanEdge> Edges) : MessagePart;
}
