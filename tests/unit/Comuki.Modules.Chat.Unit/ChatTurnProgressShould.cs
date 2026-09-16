using Comuki.Modules.Chat.Application.Ports;
using Comuki.Shared.Contracts.Chat;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Live turn progress over the real graph: the fragments the brain streams
/// reach the progress port in arrival order while the turn runs, and every
/// exit path (reply, approve interrupt, failure) emits its terminal signal —
/// the contract the SignalR fan-out on the host relies on.
/// </summary>
public sealed class ChatTurnProgressShould
{
    private static readonly Guid projectGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact(DisplayName = "Given a brain that streams fragments, when a turn runs, then each fragment reaches the progress port before the terminal signal")]
    public async Task StreamFragmentsInOrderAsync()
    {
        await using var harness = ChatHarness.Create();
        harness.Brain.Chunks.AddRange(["iteration 1: reading memory", "memory.search(\"deploy notes\")", "iteration 2: drafting"]);
        var sessionId = await harness.NewSessionAsync();

        await harness.Turns.PostAsync(
            await harness.SessionAsync(sessionId),
            "what broke yesterday?",
            TestContext.Current.CancellationToken);

        var fragments = harness.Progress.Events.Where(static streamed => !streamed.IsDone).ToList();
        fragments.Select(static streamed => streamed.Text).ShouldBe(["iteration 1: reading memory", "memory.search(\"deploy notes\")", "iteration 2: drafting"]);
        fragments.ShouldAllBe(streamed => streamed.SessionId == sessionId);
        harness.Progress.Events.Last().IsDone.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a plain turn, when it finishes, then the terminal signal is Replied")]
    public async Task SignalRepliedAfterPlainTurnAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();

        await harness.Turns.PostAsync(await harness.SessionAsync(sessionId), "hello", TestContext.Current.CancellationToken);

        harness.Progress.Events.Last().Done.ShouldBe(ChatTurnDone.Replied);
    }

    [Fact(DisplayName = "Given a plan turn that interrupts on the approve card, when it finishes, then the terminal signal is AwaitingApproval")]
    public async Task SignalAwaitingApprovalAfterInterruptAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());

        await harness.Turns.PostAsync(await harness.SessionAsync(sessionId), "fix the login bug", TestContext.Current.CancellationToken);

        harness.Progress.Events.Last().Done.ShouldBe(ChatTurnDone.AwaitingApproval);
    }

    [Fact(DisplayName = "Given a brain that is down, when the turn runs, then the fault propagates and the terminal signal is Failed")]
    public async Task SignalFailedWhenBrainIsDownAsync()
    {
        await using var harness = ChatHarness.Create();
        harness.Brain.Fault = new InvalidOperationException("brain host did not answer");
        var sessionId = await harness.NewSessionAsync();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await harness.Turns.PostAsync(
                await harness.SessionAsync(sessionId),
                "hello",
                TestContext.Current.CancellationToken));

        harness.Progress.Events.ShouldNotBeEmpty();
        harness.Progress.Events.Last().Done.ShouldBe(ChatTurnDone.Failed);
    }

    [Fact(DisplayName = "Given a pending approve, when approved, then the approve path also emits its terminal signal")]
    public async Task SignalRepliedAfterApproveAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        await harness.Turns.PostAsync(await harness.SessionAsync(sessionId), "fix the login bug", TestContext.Current.CancellationToken);

        await harness.Turns.ApproveAsync(
            await harness.SessionAsync(sessionId),
            approved: true,
            reason: null,
            TestContext.Current.CancellationToken);

        harness.Progress.Events.Last().Done.ShouldBe(ChatTurnDone.Replied);
    }

    [Fact(DisplayName = "Given streamed fragments, when the turn journals, then the thinking part carries the same fragments joined by newlines")]
    public async Task JournalMatchesStreamedFragmentsAsync()
    {
        await using var harness = ChatHarness.Create();
        harness.Brain.Chunks.AddRange(["step one", "step two"]);
        var sessionId = await harness.NewSessionAsync();

        var result = await harness.Turns.PostAsync(
            await harness.SessionAsync(sessionId),
            "hello",
            TestContext.Current.CancellationToken);

        var reply = result.NewMessages.Single(static message => message.Role == Domain.Messages.ChatMessageRole.Assistant);
        MessagePartsJson
            .TryParse(reply.PartsJson, out var parts)
            .ShouldBeTrue();
        parts.OfType<MessagePart.ThinkingPart>().Single().Text.ShouldBe("step one\nstep two");
    }
}
