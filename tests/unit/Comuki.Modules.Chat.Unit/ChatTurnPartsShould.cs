using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.Chat;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// What a journalled turn puts in the <c>parts</c> column. The rule the
/// suite defends: a message carries prose, reasoning, a tool record and a
/// plan as PARTS of one row — never as a string concatenation, and never
/// as separate rows — while <c>content</c> stays a readable projection of
/// exactly those parts.
/// </summary>
public sealed class ChatTurnPartsShould
{
    private static readonly Guid projectGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact(DisplayName = "Given a user message, when posted, then the user row carries a text part alongside its flat content")]
    public async Task JournalUserRowAsPartsAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await harness.Turns.PostAsync(session, "hello there", TestContext.Current.CancellationToken);

        var userRow = harness.Store.Messages.First(static message => message.Role == ChatMessageRole.User);
        TranscriptParts.Of(userRow).ShouldHaveSingleItem()
            .ShouldBeOfType<MessagePart.TextPart>().Markdown.ShouldBe("hello there");
    }

    [Fact(DisplayName = "Given a plan turn, when the approve card is raised, then one assistant row carries the prose and the plan as two parts")]
    public async Task JournalPlanCardAsOneRowAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var assistant = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Assistant);
        var parts = TranscriptParts.Of(assistant);
        parts.Count.ShouldBe(2);
        parts[0].ShouldBeOfType<MessagePart.TextPart>();
        parts[1].ShouldBeOfType<MessagePart.PlanPart>().Nodes.ShouldHaveSingleItem().ProfileKey.ShouldBe("implement");
    }

    [Fact(DisplayName = "Given a plan turn, when journalled, then content is the projection of the parts, not the reply with JSON glued on")]
    public async Task ProjectPlanCardIntoContentAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var assistant = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Assistant);
        assistant.Content.ShouldContain("```json");
        assistant.Content.ShouldBe(MessagePartText.Flatten(TranscriptParts.Of(assistant)));
    }

    [Fact(DisplayName = "Given brain progress fragments, when the turn is journalled, then they land as the assistant row's thinking part")]
    public async Task JournalBrainChunksAsThinkingAsync()
    {
        await using var harness = ChatHarness.Create();
        harness.Brain.Chunks.Add("reading the runs list");
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "hello there", TestContext.Current.CancellationToken);

        var assistant = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Assistant);
        TranscriptParts.Of(assistant)[0]
            .ShouldBeOfType<MessagePart.ThinkingPart>().Text.ShouldBe("reading the runs list");
    }

    [Fact(DisplayName = "Given an approved plan, when create_ticket runs, then the tool row carries the call, its status and its observation as one tool part")]
    public async Task JournalToolCallAsToolPartAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);
        await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var result = await harness.Turns.ApproveAsync(
            await harness.SessionAsync(sessionId), approved: true, reason: null, TestContext.Current.CancellationToken);

        var toolRow = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Tool);
        var tool = TranscriptParts.Of(toolRow).ShouldHaveSingleItem().ShouldBeOfType<MessagePart.ToolPart>();
        tool.Name.ShouldBe("create_ticket");
        tool.Status.ShouldBe(ToolPartStatuses.Success);
        tool.InputJson.ShouldContain(projectGuid.ToString());
        tool.OutputJson.ShouldNotBeNull().ShouldContain(FakeChatToolExecutor.RunId);
    }

    [Fact(DisplayName = "Given a brain that faults, when a turn is posted, then the fault surfaces to the caller instead of being answered with an invented reply")]
    public async Task SurfaceBrainFaultAsync()
    {
        await using var harness = ChatHarness.Create();
        harness.Brain.Fault = new InvalidOperationException("brain host unreachable");
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.Turns.PostAsync(session, "hello there", TestContext.Current.CancellationToken));
    }
}

/// <summary>Reads the parts column of a journalled row, insisting it is there.</summary>
file static class TranscriptParts
{
    /// <summary>Parsed parts of a row; fails the test when the row has none.</summary>
    /// <param name="message">Journalled transcript row.</param>
    public static IReadOnlyList<MessagePart> Of(ChatMessage message)
    {
        MessagePartsJson.TryParse(message.PartsJson, out var parts).ShouldBeTrue();
        return parts;
    }
}
