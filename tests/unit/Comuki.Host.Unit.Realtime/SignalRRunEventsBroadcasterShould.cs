using Comuki.Host.Realtime;
using Comuki.Host.Realtime.Broadcasting;
using Comuki.Host.Realtime.Models;
using Comuki.Host.Realtime.Reading;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Unit coverage of the SignalR broadcaster: run-group fan-out and attention
/// project-group fan-out (including skip when the run→project lookup misses).
/// </summary>
public sealed class SignalRRunEventsBroadcasterShould
{
    private static readonly DateTimeOffset occurredAt = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a quiet entry, when BroadcastAsync, then only the run-group RunEvent is sent")]
    public async Task BroadcastRunEventWithoutAttentionAsync()
    {
        var runId = new RunId(Guid.NewGuid());
        var entry = Entry(runId, "worker.reported", /*lang=json,strict*/ """{"stage":"tool"}""");
        var clients = Substitute.For<IHubClients>();
        var groupClient = Substitute.For<IClientProxy>();
        var hubContext = Substitute.For<IHubContext<RunsHub>>();
        _ = hubContext.Clients.Returns(clients);
        _ = clients.Group(RealtimeGroups.RunGroup(runId)).Returns(groupClient);

        var broadcaster = new SignalRRunEventsBroadcaster(
            hubContext,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRRunEventsBroadcaster>.Instance);

        await broadcaster.BroadcastAsync([entry], TestContext.Current.CancellationToken);

        await groupClient.Received(1).SendCoreAsync(
            SignalRRunEventsBroadcaster.RunEventMethod,
            Arg.Is<object?[]>(static args => IsRunEventView(args)),
            Arg.Any<CancellationToken>());
        _ = clients.DidNotReceive().Group(Arg.Is<string>(static name => name.Contains(":attention", StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "Given an attention-worthy entry with a known project, when BroadcastAsync, then Attention is sent")]
    public async Task BroadcastAttentionWhenProjectResolvedAsync()
    {
        var runId = new RunId(Guid.NewGuid());
        var projectId = new ProjectId(Guid.NewGuid());
        var itemId = Guid.NewGuid();
        var entry = Entry(
            runId,
            "work_item.status_changed",
            $$"""{"itemId":"{{itemId}}","from":"Queued","to":"Failed","attempt":1}""");

        var runGroupClient = Substitute.For<IClientProxy>();
        var attentionClient = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        var hubContext = Substitute.For<IHubContext<RunsHub>>();
        _ = hubContext.Clients.Returns(clients);
        _ = clients.Group(RealtimeGroups.RunGroup(runId)).Returns(runGroupClient);
        _ = clients.Group(RealtimeGroups.ProjectAttentionGroup(projectId)).Returns(attentionClient);

        var projects = Substitute.For<IRealtimeRunProjects>();
        _ = projects.ReadAsync(Arg.Any<IReadOnlyCollection<RunId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RunId, ProjectId> { [runId] = projectId });

        var services = new ServiceCollection();
        _ = services.AddSingleton(projects);
        await using var provider = services.BuildServiceProvider();

        var broadcaster = new SignalRRunEventsBroadcaster(
            hubContext,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRRunEventsBroadcaster>.Instance);

        await broadcaster.BroadcastAsync([entry], TestContext.Current.CancellationToken);

        await attentionClient.Received(1).SendCoreAsync(
            SignalRRunEventsBroadcaster.AttentionMethod,
            Arg.Is<object?[]>(args => IsFailedAttention(args, runId, projectId, itemId)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an attention-worthy entry whose run project is missing, when BroadcastAsync, then Attention is skipped")]
    public async Task SkipAttentionWhenProjectMissingAsync()
    {
        var runId = new RunId(Guid.NewGuid());
        var entry = Entry(
            runId,
            "work_item.status_changed",
            $$"""{"itemId":"{{Guid.NewGuid()}}","from":"Queued","to":"Running","attempt":1}""");

        var runGroupClient = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        var hubContext = Substitute.For<IHubContext<RunsHub>>();
        _ = hubContext.Clients.Returns(clients);
        _ = clients.Group(RealtimeGroups.RunGroup(runId)).Returns(runGroupClient);
        _ = clients.Group(Arg.Is<string>(static name => name.StartsWith("project:", StringComparison.Ordinal)))
            .Returns(Substitute.For<IClientProxy>());

        var projects = Substitute.For<IRealtimeRunProjects>();
        _ = projects.ReadAsync(Arg.Any<IReadOnlyCollection<RunId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RunId, ProjectId>());

        var services = new ServiceCollection();
        _ = services.AddSingleton(projects);
        await using var provider = services.BuildServiceProvider();

        var broadcaster = new SignalRRunEventsBroadcaster(
            hubContext,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalRRunEventsBroadcaster>.Instance);

        await broadcaster.BroadcastAsync([entry], TestContext.Current.CancellationToken);

        await runGroupClient.Received(1).SendCoreAsync(
            SignalRRunEventsBroadcaster.RunEventMethod,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
        _ = clients.DidNotReceive().Group(Arg.Is<string>(static name => name.Contains(":attention", StringComparison.Ordinal)));
    }

    private static bool IsRunEventView(object?[] args)
    {
        return args.Length == 1 && args[0] is RunEventView;
    }

    private static bool IsFailedAttention(object?[] args, RunId runId, ProjectId projectId, Guid itemId)
    {
        return args.Length == 1
            && args[0] is AttentionView view
            && view.RunId == runId.Value
            && view.ProjectId == projectId.Value
            && view.WorkItemId == itemId
            && view.AttentionKind == AttentionMap.KindFailed
            && view.Status == "Failed";
    }

    private static RunEventEntry Entry(RunId runId, string type, string payloadJson)
    {
        return new RunEventEntry(Guid.NewGuid(), runId, type, payloadJson, occurredAt);
    }
}
