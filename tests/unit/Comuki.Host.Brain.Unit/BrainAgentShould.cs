using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The agent loop against a scripted <see cref="IBrainChatClientFactory"/>:
/// answer termination, emit_plan termination (happy, retry-after-invalid,
/// prose-nudge) and the iteration-cap guard. The factory is the seam
/// the tests use to inject a scripted <see cref="IChatClient"/> —
/// the agent itself talks to <see cref="IModelConfigProvider"/> +
/// <see cref="IBrainChatClientFactory"/>, never to a real chat client.
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

    [Fact(DisplayName = "Given a model emitting an invalid plan then a valid one, when the loop runs, then the retry call carries the validation errors and the valid plan is accepted")]
    public async Task FeedValidationErrorsIntoTheRetryCallAsync()
    {
        const string hollow =
                             /*lang=json,strict*/
                             """{"summary":"s","nodes":[{"id":"","title":"t","profileKey":"implement","brief":"b"}],"edges":[]}""";
        var scripted = new ScriptedChatClient(
            [
                Scripted.EmitPlan("call-1", hollow),
                Scripted.EmitPlan("call-2", ValidPlan),
            ]);
        var agent = Agent(scripted);

        var chunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose"));

        // the valid second plan terminates the loop
        chunks.Last().IsFinal.ShouldBeTrue();
        chunks.Last().FinalJson.ShouldBe(ValidPlan);

        // the retry round-trip saw the rejection with the concrete errors
        scripted.Calls.Count.ShouldBe(2);
        var retryFeedback = string.Join(
            "\n",
            scripted.Calls[1]
                .SelectMany(static message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(static content => content.Result?.ToString() ?? string.Empty));
        retryFeedback.ShouldContain("plan rejected");
        retryFeedback.ShouldContain("node id must not be empty");
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
            new StaticModelConfigProvider(),
            new ScriptedChatClientFactory(Scripted.Loop("here is a plan in prose, not calling emit_plan")),
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            TimeProvider.System,
            options);

        await Should.ThrowAsync<BrainExhaustedException>(
            async () => await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose")));
    }

    [Fact(DisplayName = "Given a fake resolver returning different configs on successive calls, when two brain runs happen, then each run uses its own config")]
    public async Task ResolvePerCallAsync()
    {
        var scripted = new ScriptedChatClient(
            [
                Scripted.Text("first model reply"),
                Scripted.Text("second model reply"),
            ]);
        // Two ModelConfig values, one per call. The chat client must be
        // built from the resolved config each time; the factory below
        // records which config it was asked for.
        var factory = new RecordingChatClientFactory(scripted);
        var provider = new SequenceModelConfigProvider(
            new ModelConfig("https://api1/v4", "key-1", "model-a", "model-a"),
            new ModelConfig("https://api2/v4", "key-2", "model-b", "model-b"));

        var options = Options.Create(new BrainOptions());
        var agent = new BrainAgent(
            provider,
            factory,
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            TimeProvider.System,
            options);

        var firstChunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Answer, "first question"));
        var secondChunks = await StreamAsync(agent, Request(BrainRequestKindKeys.Answer, "second question"));

        firstChunks.ShouldHaveSingleItem().FinalJson.ShouldBe("first model reply");
        secondChunks.ShouldHaveSingleItem().FinalJson.ShouldBe("second model reply");
        factory.ConfigsSeen.ShouldBe(
        [
            new ModelConfig("https://api1/v4", "key-1", "model-a", "model-a"),
            new ModelConfig("https://api2/v4", "key-2", "model-b", "model-b"),
        ], ignoreOrder: false);
    }

    [Fact(DisplayName = "Given an answer request and a chat model id override, when the loop runs, then the chat client is built with ChatModelId")]
    public async Task ChatKindUsesChatModelIdOverrideAsync()
    {
        var factory = new RecordingChatClientFactory(new ScriptedChatClient([Scripted.Text("ok")]));
        var provider = new StaticModelConfigProvider(new ModelConfig(
            "https://api/v4",
            "key",
            "flagship-model",
            "chat-model"));

        var agent = new BrainAgent(
            provider,
            factory,
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            TimeProvider.System,
            Options.Create(new BrainOptions()));

        await StreamAsync(agent, Request(BrainRequestKindKeys.Answer, "hi"));

        factory.ConfigsSeen.ShouldHaveSingleItem().ShouldBe(new ModelConfig(
            "https://api/v4", "key", "chat-model", "chat-model"));
    }

    [Fact(DisplayName = "Given a plan request and a chat model id override, when the loop runs, then the chat client is built with the flagship ModelId")]
    public async Task PlanKindUsesFlagshipModelIdAsync()
    {
        var factory = new RecordingChatClientFactory(new ScriptedChatClient([Scripted.EmitPlan("call-1", ValidPlan)]));
        var provider = new StaticModelConfigProvider(new ModelConfig(
            "https://api/v4",
            "key",
            "flagship-model",
            "chat-model"));

        var agent = new BrainAgent(
            provider,
            factory,
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            TimeProvider.System,
            Options.Create(new BrainOptions()));

        await StreamAsync(agent, Request(BrainRequestKindKeys.Plan, "decompose"));

        factory.ConfigsSeen.ShouldHaveSingleItem().ShouldBe(new ModelConfig(
            "https://api/v4", "key", "flagship-model", "chat-model"));
    }

    private static BrainAgent Agent(params ChatResponse[] responses)
    {
        return Agent(new ScriptedChatClient(responses));
    }

    private static BrainAgent Agent(ScriptedChatClient scripted)
    {
        var options = Options.Create(new BrainOptions());
        return new BrainAgent(
            new StaticModelConfigProvider(),
            new ScriptedChatClientFactory(scripted),
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            TimeProvider.System,
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
/// response and records the message list it was called with — the
/// recording is the assertion surface for what the retry round-trip
/// actually saw. The streaming surface is unsupported (the brain loop
/// never uses it).
/// </summary>
internal sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> pending = new(responses);

    /// <summary>The message list of every GetResponseAsync call, in call order.</summary>
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add([.. messages]);
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

/// <summary>
/// <see cref="IBrainChatClientFactory"/> backed by a single
/// <see cref="ScriptedChatClient"/>. The factory is invoked once per
/// <c>BrainAgent.RunAsync</c> call (the agent builds a fresh chat
/// client at the top of each invocation).
/// </summary>
internal sealed class ScriptedChatClientFactory : IBrainChatClientFactory
{
    private readonly ScriptedChatClient scripted;

    public ScriptedChatClientFactory(params ChatResponse[] responses)
    {
        scripted = new ScriptedChatClient(responses);
    }

    public ScriptedChatClientFactory(ScriptedChatClient scripted)
    {
        this.scripted = scripted;
    }

    public IChatClient Create(ModelConfig config)
    {
        return scripted;
    }
}

/// <summary>
/// <see cref="IModelConfigProvider"/> that hands out the same
/// <see cref="ModelConfig"/> on every call — the default test fixture.
/// </summary>
internal sealed class StaticModelConfigProvider(ModelConfig config) : IModelConfigProvider
{
    public StaticModelConfigProvider()
        : this(new ModelConfig("https://api/v4", "key", "model-id", "model-id"))
    {
    }

    public ModelConfig Next { get; set; } = config;

    public Task<ModelConfig> ResolveAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Next);
    }
}

/// <summary>
/// <see cref="IModelConfigProvider"/> that pops the next
/// <see cref="ModelConfig"/> on each <see cref="ResolveAsync"/> — drives
/// the hot-reload test.
/// </summary>
internal sealed class SequenceModelConfigProvider(params ModelConfig[] configs) : IModelConfigProvider
{
    private readonly Queue<ModelConfig> queue = new(configs);

    public Task<ModelConfig> ResolveAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(queue.Dequeue());
    }
}

/// <summary>
/// <see cref="IBrainChatClientFactory"/> that records the
/// <see cref="ModelConfig"/> passed to <see cref="Create"/> and returns
/// the supplied <see cref="ScriptedChatClient"/>. The list of seen
/// configs is the assertion surface for the per-call test.
/// </summary>
internal sealed class RecordingChatClientFactory(ScriptedChatClient scripted) : IBrainChatClientFactory
{
    private readonly List<ModelConfig> seen = [];

    public IReadOnlyList<ModelConfig> ConfigsSeen => seen;

    public IChatClient Create(ModelConfig config)
    {
        seen.Add(config);
        return scripted;
    }
}
