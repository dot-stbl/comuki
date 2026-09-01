using System.Text.Json;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Messages;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Routing behaviour of the chat graph over fake ports: clarify, plan
/// approve/reject, digest injection, tool apply and slash commands
/// (issue #5 slice B).
/// </summary>
public sealed class ChatGraphShould
{
    private static readonly Guid projectGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact(DisplayName = "Given a plain message, when posted, then the brain replies and the digest is journaled")]
    public async Task ReplyToPlainMessageAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "hello there", TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeFalse();
        var reply = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Assistant);
        reply.Content.ShouldBe("brain says: hello there");
        harness.Brain.Requests.ShouldHaveSingleItem().Kind.ShouldBe("chat");
    }

    [Fact(DisplayName = "Given a task message without project scope, when posted, then the graph asks one clarifying question and skips the brain")]
    public async Task ClarifyTaskWithoutProjectAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeFalse();
        var reply = result.NewMessages.ShouldHaveSingleItem();
        reply.Role.ShouldBe(ChatMessageRole.Assistant);
        reply.Content.ShouldContain("project");
        harness.Brain.Requests.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a task message with project scope, when posted, then the approve card interrupts with the canonical plan")]
    public async Task PlanInterruptsForApprovalAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeTrue();
        result.PendingPlanJson.ShouldNotBeNullOrWhiteSpace();

        using var plan = JsonDocument.Parse(result.PendingPlanJson);
        plan.RootElement.GetProperty("nodes")[0].GetProperty("profileKey").GetString().ShouldBe("implement");

        harness.Brain.Requests.ShouldHaveSingleItem().Kind.ShouldBe("plan");
    }

    [Fact(DisplayName = "Given a pending approve, when a new message is posted, then the turn is refused with a pending exception")]
    public async Task RefuseTurnWhileApprovePendingAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);
        await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var pending = await harness.SessionAsync(sessionId);
        await Should.ThrowAsync<ChatApprovePendingException>(
            () => harness.Turns.PostAsync(pending, "another message", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a pending approve, when approved, then create_ticket applies the plan and the run id lands in the reply")]
    public async Task ApproveAppliesPlanAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);
        await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var result = await harness.Turns.ApproveAsync(
            await harness.SessionAsync(sessionId), approved: true, reason: null, TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeFalse();
        var toolCall = harness.Tools.Calls.ShouldHaveSingleItem();
        toolCall.Name.ShouldBe("create_ticket");

        using var arguments = JsonDocument.Parse(toolCall.ArgumentsJson);
        arguments.RootElement.GetProperty("projectId").GetString().ShouldBe(projectGuid.ToString());

        result.NewMessages.ShouldContain(static message => message.Role == ChatMessageRole.Tool);
        result.NewMessages.ShouldContain(static message =>
            message.Role == ChatMessageRole.Assistant && message.Content.Contains(FakeChatToolExecutor.RunId));
    }

    [Fact(DisplayName = "Given a pending approve, when rejected with a reason, then nothing is queued and the reason is echoed")]
    public async Task RejectSkipsApplyAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);
        await harness.Turns.PostAsync(session, "fix the login bug", TestContext.Current.CancellationToken);

        var result = await harness.Turns.ApproveAsync(
            await harness.SessionAsync(sessionId), approved: false, reason: "wrong repo", TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeFalse();
        harness.Tools.Calls.ShouldBeEmpty();
        result.NewMessages.ShouldContain(static message =>
            message.Role == ChatMessageRole.Assistant && message.Content.Contains("wrong repo"));
    }

    [Fact(DisplayName = "Given a non-empty digest, when the brain is invoked, then the digest rides in the context and is journaled as a system row")]
    public async Task InjectDigestIntoBrainContextAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync(projectGuid.ToString());
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "hello there", TestContext.Current.CancellationToken);

        var request = harness.Brain.Requests.ShouldHaveSingleItem();
        using var context = JsonDocument.Parse(request.ContextJson);
        context.RootElement.GetProperty("digest").GetString().ShouldBe(FakeMemoryDigest.DefaultDigest);

        var digestRow = result.NewMessages.Single(static message => message.Role == ChatMessageRole.System);
        digestRow.Content.ShouldContain(FakeMemoryDigest.DefaultDigest);

        harness.Digest.Requests.ShouldHaveSingleItem().ScopeKind.ShouldBe(Shared.Contracts.Memory.MemoryDigestScopes.Project);
    }

    [Fact(DisplayName = "Given a second turn, when posted, then the prior turn does not leak into the new reply")]
    public async Task SecondTurnIsCleanAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);
        await harness.Turns.PostAsync(session, "first message", TestContext.Current.CancellationToken);

        var result = await harness.Turns.PostAsync(
            await harness.SessionAsync(sessionId), "second message", TestContext.Current.CancellationToken);

        result.AwaitingApproval.ShouldBeFalse();
        result.NewMessages.ShouldNotContain(static message => message.Content.Contains("first message"));
        var assistantRows = result.NewMessages.Where(static message => message.Role == ChatMessageRole.Assistant).ToList();
        assistantRows.ShouldHaveSingleItem().Content.ShouldBe("brain says: second message");
    }

    [Fact(DisplayName = "Given a known slash command with arguments, when posted, then the command body and arguments form the brain task")]
    public async Task ExpandKnownSlashCommandAsync()
    {
        var commands = new[]
        {
            new ChatCommandDefinition("restart", "Restart", "Restart the run.", "Restart the current run now."),
        };
        await using var harness = ChatHarness.Create(commands);
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await harness.Turns.PostAsync(session, "/restart because it hung", TestContext.Current.CancellationToken);

        var request = harness.Brain.Requests.ShouldHaveSingleItem();
        request.Kind.ShouldBe("chat");
        request.Task.ShouldContain("Restart the current run now.");
        request.Task.ShouldContain("because it hung");
    }

    [Fact(DisplayName = "Given an unknown slash command, when posted, then it falls through to the brain as chat")]
    public async Task UnknownSlashCommandFallsThroughAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await harness.Turns.PostAsync(session, "/nosuchcommand arg", TestContext.Current.CancellationToken);

        var request = harness.Brain.Requests.ShouldHaveSingleItem();
        request.Kind.ShouldBe("chat");
        request.Task.ShouldContain("/nosuchcommand");
    }

    [Fact(DisplayName = "Given the slash catalog, when listed, then built-ins and control-plane commands merge ordered with built-ins winning collisions")]
    public async Task MergeSlashCatalogAsync()
    {
        var commands = new[]
        {
            new ChatCommandDefinition("help", "Rogue help", "Collision must lose.", "rogue body"),
            new ChatCommandDefinition("restart", "Restart", "Restart the run.", "Restart the current run now."),
        };
        var catalog = new Application.Slash.ChatSlashCatalog(new FakeChatCommandCatalog(commands));

        var merged = await catalog.ListAsync(TestContext.Current.CancellationToken);

        merged.Select(static command => command.Key).ShouldBe(["help", "init", "restart"]);
        merged.Single(static command => command.Key == "help").Source.ShouldBe(Application.Slash.ChatSlashSources.Builtin);
        merged.Single(static command => command.Key == "restart").Source.ShouldBe(Application.Slash.ChatSlashSources.ControlPlane);
    }
}
