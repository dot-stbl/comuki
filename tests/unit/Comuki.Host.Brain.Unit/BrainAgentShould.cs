using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Shared.Contracts.Brain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The agent loop against a scripted IChatClient: answer termination,
/// emit_plan termination (happy, retry-after-invalid, prose-nudge) and
/// the iteration-cap guard.
/// </summary>
public sealed class BrainAgentShould
{
    private const string ValidPlan =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"implement","brief":"b"}],"edges":[]}""";

    [Fact(DisplayName = "Given an answer request and a plain-text model response, when the loop runs, then the final chunk carries the text")]
    public async Task FinishAnswerWithFinalChunkAsync()
    {
        var agent = Agent([Scripted.Text("the port pool is 17000-17200")]);

        var chunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Answer, "which ports?"));

        chunks.ShouldHaveSingleItem().IsFinal.ShouldBeTrue();
        chunks.ShouldHaveSingleItem().FinalJson.ShouldBe("the port pool is 17000-17200");
    }

    [Fact(DisplayName = "Given a plan request and a model that emits a valid plan, when the loop runs, then the final chunk carries the plan json")]
    public async Task FinishPlanOnEmitPlanAsync()
    {
        var agent = Agent([Scripted.EmitPlan("call-1", ValidPlan)]);

        var chunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose"));

        chunks.Last().IsFinal.ShouldBeTrue();
        chunks.Last().FinalJson.ShouldBe(ValidPlan);
        chunks.First().Text.ShouldContain("emit_plan");
    }

    [Fact(DisplayName = "Given a plan request and a model answering in prose first, when nudged and then emitting a plan, then the run still finishes with the plan")]
    public async Task NudgeProseAnswerThenAcceptPlanAsync()
    {
        var agent = Agent(
            [
                Scripted.Text("here is a plan in prose"),
                Scripted.EmitPlan("call-1", ValidPlan),
            ]);

        var chunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose"));

        chunks.Last().FinalJson.ShouldBe(ValidPlan);
    }

    [Fact(DisplayName = "Given a model emitting an invalid plan then a valid one, when the loop runs, then the retry succeeds")]
    public async Task RetryInvalidPlanThenSucceedAsync()
    {
        const string cyclic =
                                 /*lang=json,strict*/
                                 """{"summary":"s","nodes":[{"id":"n1","title":"t","profileKey":"implement","brief":"b"},{"id":"n2","title":"t","profileKey":"implement","brief":"b"}],"edges":[{"from":"n1","to":"n2"},{"from":"n2","to":"n1"}]}""";
        var agent = Agent(
            [
                Scripted.EmitPlan("call-1", cyclic),
                Scripted.EmitPlan("call-2", ValidPlan),
            ]);

        var chunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose"));

        chunks.Last().FinalJson.ShouldBe(ValidPlan);
    }

    [Fact(DisplayName = "Given a model whose plan stays invalid, when the loop runs, then BrainInvalidPlanException fails the call")]
    public async Task FailWhenPlanStaysInvalidAsync()
    {
        const string bad = /*lang=json,strict*/ """{"summary":"s","nodes":[],"edges":[]}""";
        var agent = Agent([Scripted.EmitPlan("call-1", bad), Scripted.EmitPlan("call-2", bad)]);

        var exception = await Should.ThrowAsync<BrainInvalidPlanException>(
            async () => await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose")));

        exception.Errors.ShouldContain("plan must contain at least one node");
    }

    [Fact(DisplayName = "Given a model that never finishes, when the iteration cap hits, then BrainExhaustedException fails the call")]
    public async Task FailWhenIterationsRunOutAsync()
    {
        var options = Options.Create(new BrainOptions { MaxToolIterations = 2 });
        var agent = new BrainAgent(
            Scripted.Loop("here is a plan in prose, not calling emit_plan"),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            options);

        await Should.ThrowAsync<BrainExhaustedException>(
            async () => await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose")));
    }

    private static BrainAgent Agent(params ChatResponse[] responses)
    {
        var options = Options.Create(new BrainOptions());
        return new BrainAgent(
            new ScriptedChatClient(responses),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            options);
    }

    private static BrainRequest Request(string kind, string task)
    {
        return new BrainRequest { Kind = kind, Task = task, ContextJson = "{}" };
    }

    private static async Task<List<BrainChunk>> StreamAsync(BrainAgent agent, BrainRequest request)
    {
        var chunks = new List<BrainChunk>();
        await foreach (var chunk in agent.RunAsync(request, TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}

/// <summary>Scripted response builders for the fake chat client.</summary>
internal static class Scripted
{
    public static ChatResponse Text(string text)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    public static ChatResponse EmitPlan(string callId, string planJson)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, "emit_plan", new Dictionary<string, object?> { ["planJson"] = planJson })])]);
    }

    public static ScriptedChatClient Loop(string text)
    {
        return new ScriptedChatClient([.. Enumerable.Repeat(Text(text), 100)]);
    }
}

/// <summary>
/// IChatClient fake: each GetResponseAsync pops the next scripted
/// response; the streaming surface is unsupported (the brain loop never
/// uses it).
/// </summary>
internal sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> pending = new(responses);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(pending.Dequeue());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("the brain agent loop never streams");
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    void IDisposable.Dispose()
    {
    }
}
