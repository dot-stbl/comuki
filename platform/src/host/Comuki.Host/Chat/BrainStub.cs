using System.Text.Json;
using Comuki.Shared.Contracts.Brain;

namespace Comuki.Host.Chat;

/// <summary>
/// In-process brain fallback until the dedicated brain host
/// (Comuki.Host.Brain, gRPC — sibling slice) lands: plan invocations return
/// a deterministic single-item implement plan carrying the task as the
/// brief; chat invocations echo the task. Registered with TryAdd so a real
/// client registration always wins.
/// </summary>
public sealed class BrainStub : IBrainClient
{
    /// <summary>Single-item plan the stub produces for plan invocations.</summary>
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
            new StubPlan([new StubPlanItem("step-1", BrainStub.StubProfileKey, task, [])]),
            JsonSerializerOptions.Web);
    }
}

/// <summary>Stub plan wire shape (camelCase).</summary>
/// <param name="Items"></param>
internal sealed record StubPlan(IReadOnlyList<StubPlanItem> Items);

/// <summary>Stub plan item wire shape (camelCase).</summary>
/// <param name="Key"></param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
/// <param name="DependsOn"></param>
internal sealed record StubPlanItem(string Key, string ProfileKey, string Brief, IReadOnlyList<string> DependsOn);
