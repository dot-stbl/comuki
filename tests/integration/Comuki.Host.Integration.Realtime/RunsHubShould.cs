using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Host.Realtime;
using Comuki.Host.Realtime.Models;
using Comuki.Shared.Kernel.Ids;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Realtime;

/// <summary>
/// End-to-end realtime suite over the real host composition: hub join
/// permissions, the journal broadcast interceptor (every writer's append
/// reaches the run group) and the project attention fan-out on status
/// transitions.
/// </summary>
public sealed class RunsHubShould(HostRealtimeServer server) : IClassFixture<HostRealtimeServer>
{
    [Fact(DisplayName = "Given a joined run group, when a journal event is appended, then the client receives the slim view")]
    public async Task ReceiveJournalEventAfterJoinAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var (runId, workItemId, profileKey) = await server.SeedRunAsync(projectId);

        await using var connection = await server.ConnectHubAsync(
            HostRealtimeServer.BootstrapEmail,
            HostRealtimeServer.BootstrapPassword);

        var received = new TaskCompletionSource<RunEventView>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On(
            "RunEvent",
            (RunEventView view) => received.SetResult(view));

        // Hub methods must not take CancellationToken — the client would
        // serialise it as an argument. Pass only the run id.
#pragma warning disable xUnit1051
        await connection.InvokeAsync("JoinRunAsync", runId.Value);
#pragma warning restore xUnit1051
        await server.ClaimAsync(runId, workItemId, profileKey);

        var view = await received.Task.WaitAsync(HostRealtimeServer.HubTimeout, TestContext.Current.CancellationToken);

        view.RunId.ShouldBe(runId.Value);
        view.Type.ShouldBe("work_item.status_changed");
        view.WorkItemId.ShouldBe(workItemId);
        view.PayloadOmitted.ShouldBeFalse();
        view.PayloadJson.ShouldNotBeNull();
        view.PayloadJson.ShouldContain("\"to\":\"Running\"");
    }

    [Fact(DisplayName = "Given no credentials, when the hub handshake runs, then the connection is refused")]
    public async Task RefuseAnonymousHandshakeAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(server.HubAddress)
            .Build();

        var exception = await Should.ThrowAsync<Exception>(
            async () => await connection.StartAsync(TestContext.Current.CancellationToken));

        exception.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given a member with no project access, when joining a run group, then the join is denied and no group is entered")]
    public async Task RefuseJoinWithoutPermissionAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var (runId, _, _) = await server.SeedRunAsync(projectId);

        await using var connection = await server.ConnectHubAsync(
            HostRealtimeServer.MemberEmail,
            HostRealtimeServer.MemberPassword);

#pragma warning disable xUnit1051
        var exception = await Should.ThrowAsync<HubException>(
            async () => await connection.InvokeAsync("JoinRunAsync", runId.Value));
#pragma warning restore xUnit1051

        exception.Message.ShouldContain(JoinErrors.PermissionDenied);
    }

    [Fact(DisplayName = "Given a joined project attention group, when a work item starts, then the client receives the attention event")]
    public async Task BroadcastAttentionOnStatusTransitionAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var (runId, workItemId, profileKey) = await server.SeedRunAsync(projectId);

        await using var connection = await server.ConnectHubAsync(
            HostRealtimeServer.BootstrapEmail,
            HostRealtimeServer.BootstrapPassword);

        var received = new TaskCompletionSource<AttentionView>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On(
            "Attention",
            (AttentionView view) => received.SetResult(view));

#pragma warning disable xUnit1051
        await connection.InvokeAsync("JoinProjectAsync", projectId.Value);
#pragma warning restore xUnit1051
        await server.ClaimAsync(runId, workItemId, profileKey);

        var attention = await received.Task.WaitAsync(HostRealtimeServer.HubTimeout, TestContext.Current.CancellationToken);

        attention.RunId.ShouldBe(runId.Value);
        attention.ProjectId.ShouldBe(projectId.Value);
        attention.WorkItemId.ShouldBe(workItemId);
        attention.Status.ShouldBe("Running");
        attention.AttentionKind.ShouldBe(AttentionMap.KindRunning);
    }

    [Fact(DisplayName = "Given a joined project attention group, when the run enters Waiting, then the client receives an awaiting_approval attention event")]
    public async Task BroadcastAttentionOnRunAwaitingApprovalAsync()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var (runId, _, _) = await server.SeedRunAsync(projectId);

        await using var connection = await server.ConnectHubAsync(
            HostRealtimeServer.BootstrapEmail,
            HostRealtimeServer.BootstrapPassword);

        var received = new TaskCompletionSource<AttentionView>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On(
            "Attention",
            (AttentionView view) => received.SetResult(view));

#pragma warning disable xUnit1051
        await connection.InvokeAsync("JoinProjectAsync", projectId.Value);
#pragma warning restore xUnit1051

        // Simulate the orchestration handler appending a run.status_changed
        // journal row through the same SaveChangesAsync pipeline the
        // interceptor observes — the broadcaster reads the run group
        // independently and forwards the attention half via the project
        // group the client already joined.
        await AppendRunEventAsync(
            runId,
            RunEventTypes.RunStatusChanged,
            /*lang=json,strict*/ """{"to":"Waiting","from":"Queued"}""");

        var attention = await received.Task.WaitAsync(HostRealtimeServer.HubTimeout, TestContext.Current.CancellationToken);

        attention.RunId.ShouldBe(runId.Value);
        attention.ProjectId.ShouldBe(projectId.Value);
        attention.Status.ShouldBe("Waiting");
        attention.AttentionKind.ShouldBe(AttentionMap.KindAwaitingApproval);
    }

    [Fact(DisplayName = "Given an unknown run id, when joining the run group, then the join is refused and no group is entered")]
    public async Task RefuseJoinUnknownRunAsync()
    {
        await using var connection = await server.ConnectHubAsync(
            HostRealtimeServer.BootstrapEmail,
            HostRealtimeServer.BootstrapPassword);

#pragma warning disable xUnit1051
        var exception = await Should.ThrowAsync<HubException>(
            async () => await connection.InvokeAsync("JoinRunAsync", Guid.NewGuid()));
#pragma warning restore xUnit1051

        exception.Message.ShouldContain(JoinErrors.RunNotFound);
    }

    /// <summary>
    /// Appends one journal row through the host's orchestration DbContext so
    /// the broadcast interceptor sees it. Keeps the run→project lookup path
    /// warm without driving a real orchestration handler.
    /// </summary>
    private async Task AppendRunEventAsync(RunId runId, string type, string payloadJson)
    {
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext>();
        using var _ = scope.ServiceProvider
            .GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("realtime-fixture");

        db.RunEvents.Add(RunEvent.Create(runId, type, payloadJson, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
