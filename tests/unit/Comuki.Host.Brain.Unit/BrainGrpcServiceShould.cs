using Comuki.Host.Brain.Ports;
using Comuki.Shared.Contracts.Brain;
using Grpc.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// Request validation and fault mapping on the gRPC surface: bad kind and
/// empty task become InvalidArgument; a plan that stays invalid after its
/// retry becomes Internal (the IBrainService contract).
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

    [Fact(DisplayName = "Given a model whose plan stays invalid, when Think runs, then RpcException is Internal carrying the validation errors")]
    public async Task MapInvalidPlanToInternalFaultAsync()
    {
        const string bad = /*lang=json,strict*/ """{"summary":"s","nodes":[],"edges":[]}""";
        var agent = Agent(Scripted.EmitPlan("call-1", bad), Scripted.EmitPlan("call-2", bad));
        var service = Service(agent);

        var exception = await Should.ThrowAsync<RpcException>(
            async () => await DrainAsync(service, new BrainRequest { Kind = BrainRequestKindKeys.Plan, Task = "decompose" }));

        exception.StatusCode.ShouldBe(StatusCode.Internal);
        exception.Status.Detail.ShouldContain("plan must contain at least one node");
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
            new ScriptedChatClient(responses),
            new FakeMemoryStore([]),
            new FakeProfileCatalog([new("implement", "Implementer", "writes the code", [], null)]),
            new StubActiveRunCatalog(),
            new StubExplorerReportReader(),
            Options.Create(new BrainOptions()));
    }

    private static BrainGrpcService Service(BrainAgent agent)
    {
        return new BrainGrpcService(agent, NullLogger<BrainGrpcService>.Instance);
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
