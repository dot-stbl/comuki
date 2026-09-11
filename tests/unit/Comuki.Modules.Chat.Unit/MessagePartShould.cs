using System.Text.Json;
using Comuki.Shared.Contracts.Chat;
using Comuki.Shared.Contracts.Plans;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// The frozen message-part contract: a discriminated union serialized with
/// a <c>kind</c> property, tolerant to payloads a newer producer wrote,
/// and projectable onto one flat markdown string — the projection the
/// <c>content</c> column keeps so the digest, search and pre-parts clients
/// never read an empty row.
/// </summary>
public sealed class MessagePartShould
{
    [Theory(DisplayName = "Given a part of each kind, when serialized, then the wire carries its frozen kind discriminator")]
    [InlineData(MessagePartKinds.Text)]
    [InlineData(MessagePartKinds.Code)]
    [InlineData(MessagePartKinds.Diagram)]
    [InlineData(MessagePartKinds.Thinking)]
    [InlineData(MessagePartKinds.Tool)]
    [InlineData(MessagePartKinds.Handoff)]
    [InlineData(MessagePartKinds.Plan)]
    public void DiscriminateEveryKind(string kind)
    {
        var json = MessagePartsJson.Serialize([SamplePart.Of(kind)]);

        using var document = JsonDocument.Parse(json);
        document.RootElement[0].GetProperty("kind").GetString().ShouldBe(kind);
    }

    [Fact(DisplayName = "Given a mixed part list, when round-tripped, then every part comes back as its own kind in order")]
    public void RoundTripMixedParts()
    {
        IReadOnlyList<MessagePart> parts =
        [
            new MessagePart.TextPart("here is the plan"),
            new MessagePart.ToolPart(
                "create_ticket",
                /*lang=json,strict*/ """{"projectId":"p"}""",
                ToolPartStatuses.Success,
                /*lang=json,strict*/ """{"runId":"r"}""",
                12),
            new MessagePart.PlanPart([new PlanNode("n1", "Do it", "implement", "brief")], []),
        ];

        MessagePartsJson.TryParse(MessagePartsJson.Serialize(parts), out var parsed).ShouldBeTrue();

        parsed.ShouldNotBeNull();
        parsed.Count.ShouldBe(3);
        parsed[0].ShouldBeOfType<MessagePart.TextPart>().Markdown.ShouldBe("here is the plan");
        parsed[1].ShouldBeOfType<MessagePart.ToolPart>().DurationMs.ShouldBe(12);
        parsed[2].ShouldBeOfType<MessagePart.PlanPart>().Nodes.ShouldHaveSingleItem().Id.ShouldBe("n1");
    }

    [Fact(DisplayName = "Given a payload with a kind this build does not know, when parsed, then the read is refused rather than faulted")]
    public void RefuseUnknownKind()
    {
        MessagePartsJson.TryParse(
            /*lang=json,strict*/ """[{"kind":"question","prompt":"which repo?"}]""",
            out var parsed).ShouldBeFalse();

        parsed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an absent or malformed payload, when parsed, then the read is refused rather than faulted")]
    public void RefuseMalformedPayload()
    {
        MessagePartsJson.TryParse("not json at all", out var parsed).ShouldBeFalse();

        parsed.ShouldBeNull();
    }

    [Fact(DisplayName = "Given metadata, when round-tripped, then every cost field survives")]
    public void RoundTripMeta()
    {
        var meta = new ChatMessageMeta("glm-4.7", 120, 340, 1500, 820, "stop");

        MessagePartsJson.TryParseMeta(MessagePartsJson.SerializeMeta(meta), out var parsed).ShouldBeTrue();

        parsed.ShouldBe(meta);
    }

    [Fact(DisplayName = "Given prose and a plan in one message, when flattened, then the projection keeps both as separate readable blocks")]
    public void FlattenProseAndPlan()
    {
        IReadOnlyList<MessagePart> parts =
        [
            new MessagePart.TextPart("Here is the plan — approve it."),
            new MessagePart.PlanPart([new PlanNode("n1", "Do it", "implement", "brief")], []),
        ];

        var flat = MessagePartText.Flatten(parts);

        flat.ShouldStartWith("Here is the plan — approve it.");
        flat.ShouldContain("```json");
        flat.ShouldContain("\"profileKey\":\"implement\"");
    }

    [Fact(DisplayName = "Given a code part with a repository anchor, when flattened, then the anchor and a fenced listing survive")]
    public void FlattenAnchoredCode()
    {
        var flat = MessagePartText.Flatten(
            [new MessagePart.CodePart("csharp", "var x = 1;", "src/Program.cs", 42)]);

        flat.ShouldContain("`src/Program.cs:42`");
        flat.ShouldContain("```csharp\nvar x = 1;\n```");
    }

    [Fact(DisplayName = "Given a projection longer than the column bound, when clamped, then it fits and the cut is marked")]
    public void ClampToColumnBound()
    {
        var clamped = MessagePartText.Clamp(new string('a', 64), 16);

        clamped.Length.ShouldBe(16);
        clamped.ShouldEndWith(MessagePartText.TruncationMarker);
    }
}

/// <summary>One representative part per wire kind.</summary>
file static class SamplePart
{
    /// <summary>Builds the sample for a kind key.</summary>
    /// <param name="kind">A <see cref="MessagePartKinds"/> value.</param>
    public static MessagePart Of(string kind)
    {
        return kind switch
        {
            MessagePartKinds.Text => new MessagePart.TextPart("prose"),
            MessagePartKinds.Code => new MessagePart.CodePart("csharp", "var x = 1;"),
            MessagePartKinds.Diagram => new MessagePart.DiagramPart("mermaid", "graph TD; a-->b;"),
            MessagePartKinds.Thinking => new MessagePart.ThinkingPart("weighing two options", 41),
            MessagePartKinds.Tool => new MessagePart.ToolPart("runs", "{}", ToolPartStatuses.Running),
            MessagePartKinds.Handoff => new MessagePart.HandoffPart("run:01J"),
            _ => new MessagePart.PlanPart([], []),
        };
    }
}
