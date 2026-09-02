using System.Text.Json;
using Comuki.Shared.Contracts.Brain;

namespace Comuki.Host.Chat.Brain;

/// <summary>
/// In-process brain fallback until the dedicated brain host
/// (Comuki.Host.Brain, gRPC — sibling slice) is configured: plan invocations
/// return a deterministic single-node implement plan carrying the task as
/// the brief; chat invocations echo the task. Registered with TryAdd so a
/// real client registration always wins. Emits the canonical Plan contract
/// shape (summary/nodes/edges).
/// </summary>
public sealed class BrainStub : IBrainClient
{
    /// <summary>Single-node plan profile the stub produces for plan invocations.</summary>
    public const string StubProfileKey = "implement";

    /// <inheritdoc />
    public Task<BrainReply> InvokeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reply = request.Kind == "plan"
            ? new BrainReply([], StubPlanJson.Of(request.Task))
            : new BrainReply([], "stub brain: " + request.Task);
        return Task.FromResult(reply);
    }
}

file static class StubPlanJson
{
    public static string Of(string task)
    {
        return JsonSerializer.Serialize(
            new StubPlan("stub plan", [new StubPlanNode("n1", "Do it", BrainStub.StubProfileKey, task)], []),
            JsonSerializerOptions.Web);
    }
}

/// <summary>Stub plan wire shape (camelCase) matching the Plan contract.</summary>
/// <param name="Summary"></param>
/// <param name="Nodes"></param>
/// <param name="Edges"></param>
internal sealed record StubPlan(string Summary, IReadOnlyList<StubPlanNode> Nodes, IReadOnlyList<StubPlanEdge> Edges);

/// <summary>Stub plan node wire shape.</summary>
/// <param name="Id"></param>
/// <param name="Title"></param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
internal sealed record StubPlanNode(string Id, string Title, string ProfileKey, string Brief);

/// <summary>Stub plan edge wire shape (empty in the stub).</summary>
/// <param name="From"></param>
/// <param name="To"></param>
internal sealed record StubPlanEdge(string From, string To);
