using Comuki.Host.Brain.Brain;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Scoping;
using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Request validation and fault mapping on the gRPC surface: bad kind and
/// empty task become InvalidArgument; a plan that stays invalid after its
/// retry degrades to a final answer chunk carrying the validation errors
/// (no Internal fault — the caller would surface it as a 503).
/// </summary>
public sealed class BrainGrpcServiceShould
{
    [Fact(DisplayName = "Given an unknown kind, when Think runs, then RpcException is InvalidArgument naming the expected kinds")]
    public async Task RefuseUnknownKindAsync()
    {
        var service = Service(Agent(Scripted.Text("unused")));

        var exception = await Should.ThrowAsync<RpcException>(
            async () => await DrainAsync(service, new BrainRequest { Kind = "dream", Task = "t" }));

        exception.StatusCode.ShouldBe(StatusCode.InvalidArgument);
        exception.Status.Detail.ShouldContain("plan|brief|repair|answer");
    }

    [Theory(DisplayName = "Given an empty task, when Think runs, then RpcException is InvalidArgument")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefuseEmptyTaskAsync(string task)
    {
        var service = Service(Agent(Scripted.Text("unused")));

        var exception = await Should.ThrowAsync<RpcException>(
            async () => await DrainAsync(service, new BrainRequest { Kind = BrainRequestKindKeys.Answer, Task = task }));

        exception.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact(DisplayName = "Given a model whose plan stays invalid, when Think runs, then the stream ends with a final chunk carrying the validation errors and a rephrase suggestion")]
    public async Task AnswerWithPlanErrorsWhenPlanStaysInvalidAsync()
    {
        const string bad = /*lang=json,strict*/ """{"summary":"s","nodes":[],"edges":[]}""";
        var agent = Agent(Scripted.EmitPlan("call-1", bad), Scripted.EmitPlan("call-2", bad));
        var service = Service(agent);

        var chunks = await DrainAsync(service, new BrainRequest { Kind = BrainRequestKindKeys.Plan, Task = "decompose" });

        var final = chunks.Last();
        final.IsFinal.ShouldBeTrue();
        final.FinalJson.ShouldContain("plan must contain at least one node");
        final.FinalJson.ShouldContain("rephrase");
    }

    [Fact(DisplayName = "Given an answer request, when Think runs, then the final chunk streams out unchanged")]
    public async Task StreamAnswerChunksAsync()
    {
        var service = Service(Agent(Scripted.Text("here is the answer")));

        var chunks = await DrainAsync(service, new BrainRequest { Kind = BrainRequestKindKeys.Answer, Task = "q" });

        chunks.ShouldHaveSingleItem().FinalJson.ShouldBe("here is the answer");
    }

    private static BrainAgent Agent(params ChatResponse[] responses)
    {
        return new BrainAgent(
            TimeProvider.System,
            Substitute.For<IMemoryDigest>(),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StaticModelConfigProvider(),
            new StubExplorerReportReader(),
            new AsyncLocalSubjectScopeAccessor(),
            Options.Create(new BrainOptions()),
            new ScriptedChatClientFactory(responses));
    }

    private static BrainGrpcService Service(BrainAgent agent)
    {
        return new BrainGrpcService(agent, new AsyncLocalSubjectScopeAccessor(), NullLogger<BrainGrpcService>.Instance);
    }
    private static async Task<List<BrainChunk>> DrainAsync(BrainGrpcService service, BrainRequest request)
    {
        var chunks = new List<BrainChunk>();
        await foreach (var chunk in service.Think(request, default))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}
