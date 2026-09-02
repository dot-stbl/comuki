using Comuki.Engine.Orchestration.Domain;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;
using static Comuki.Host.Integration.Intake.WebhooksShould;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// The two-way sync on the real composition: a finished run is bridged
/// into the outbox, drained into the (fake) provider transition port,
/// and releases the one-live-run lock so the issue can run again.
/// </summary>
public sealed class SyncBackShould(HostIntakeServer server) : IClassFixture<HostIntakeServer>
{
    private readonly HostIntakeServer server = server;

    [Fact(DisplayName = "Given a claimed ticket whose run succeeds, when the bridge cycles, then the fake provider gets the transition and the lock releases")]
    public async Task DrainOutboxToProviderAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        using var anonymous = server.CreateAnonymousClient();

        var projectId = ProjectId.New().Value;
        var webhookPath = await CreateConnectionAsync(browser, projectId, "sync hook");
        await CreateRuleAsync(browser, projectId, "watch", /*lang=json,strict*/ """{"labelsAny": ["comuki"]}""");

        var delivery = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-opened.json"), "sync-" + Guid.NewGuid().ToString("N"));
        (await OutcomeOfAsync(delivery)).ShouldBe("admitted");

        // drive the run to terminal (Queued → Running → Succeeded)
        var scope = new ProjectId(projectId);
        RunId runId;
        await using (var orchestration = server.CreateOrchestrationDb())
        {
            var run = await orchestration.Runs.SingleAsync(candidate => candidate.ProjectId == scope, cancellationToken);
            run.TransitionTo(RunStatus.Running, DateTimeOffset.UtcNow);
            run.TransitionTo(RunStatus.Succeeded, DateTimeOffset.UtcNow);
            await orchestration.SaveChangesAsync(cancellationToken);
            runId = run.Id;
        }

        // the bridge enqueues + drains and the fake records the push
        await HostIntakeServer.WaitForAsync(
            () => Task.FromResult(server.GithubSync.Transitions.Count > 0),
            TimeSpan.FromSeconds(20));

        var transition = server.GithubSync.Transitions.Single();
        transition.ExternalId.ShouldBe("dot-stbl/comuki#481");
        transition.RunStatus.ShouldBe("Succeeded");
        transition.RunUrl.ToString().ShouldEndWith("runs/" + runId.Value);

        // the job is done and the ticket released
        await HostIntakeServer.WaitForAsync(async () =>
        {
            await using var intake = server.CreateIntakeDb();
            var job = await intake.SyncJobs.AsNoTracking().SingleAsync(candidate => candidate.RunId == runId, cancellationToken);
            return job.Status == SyncJobStatus.Done;
        },
        TimeSpan.FromSeconds(10));

        await using var released = server.CreateIntakeDb();
        var ticket = await released.Tickets.AsNoTracking().SingleAsync(
            candidate => candidate.ExternalId == "dot-stbl/comuki#481" && candidate.RunId == runId,
            cancellationToken);
        ticket.Status.ShouldBe(IntakeTicketStatus.Done);

        // the lock released: the same issue can start a fresh run
        var again = await PostWebhookAsync(
            anonymous, webhookPath, await HostIntakeFiles.ReadFixtureAsync("github-issue-labeled.json"), "sync-2-" + Guid.NewGuid().ToString("N"));
        (await OutcomeOfAsync(again)).ShouldBe("admitted");
    }
}
