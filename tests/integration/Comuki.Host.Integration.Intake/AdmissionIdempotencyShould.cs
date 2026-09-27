using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// WS9 (issue #87): the Orchestration-side admission guarantee — a
/// retried or concurrently-duplicated admission call for the SAME
/// ticket identity creates at most one Run, and the losing/retried
/// caller observes the winner's Run id. Exercises <see cref="IRunLauncher"/>
/// directly (not the webhook HTTP surface) because Intake's own
/// delivery-id / active-ticket locks already prevent the webhook path
/// from ever calling <see cref="IRunLauncher.LaunchAsync"/> twice for a
/// genuinely identical delivery — this test proves the launcher itself
/// is safe regardless of what calls it (design.md decision #8:
/// "Orchestration-side guarantee only").
/// </summary>
[Collection(nameof(IntakeHostCollection))]
public sealed class AdmissionIdempotencyShould(HostIntakeServer server)
{
    private readonly HostIntakeServer server = server;

    [Fact(DisplayName = "Given the same admission ticket, when launched twice sequentially, then exactly one run exists and both calls return it")]
    public async Task SequentialRetryCreatesOneRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = ProjectId.New();
        var ticket = NewTicket(projectId);

        var first = await LaunchAsync(projectId, ticket, cancellationToken);
        var second = await LaunchAsync(projectId, ticket, cancellationToken);

        second.ShouldBe(first);
        (await RunCountAsync(projectId, cancellationToken)).ShouldBe(1);
    }

    [Fact(DisplayName = "Given the same admission ticket, when launched concurrently, then exactly one run exists and both calls observe the same id")]
    public async Task ConcurrentRaceCreatesOneRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = ProjectId.New();
        var ticket = NewTicket(projectId);

        var firstTask = LaunchAsync(projectId, ticket, cancellationToken);
        var secondTask = LaunchAsync(projectId, ticket, cancellationToken);
        var results = await Task.WhenAll(firstTask, secondTask);

        results[0].ShouldBe(results[1]);
        (await RunCountAsync(projectId, cancellationToken)).ShouldBe(1);
    }

    private async Task<RunId> LaunchAsync(ProjectId projectId, IncomingTicket ticket, CancellationToken cancellationToken)
    {
        using var scope = server.Services.CreateScope();
        var launcher = scope.ServiceProvider.GetRequiredService<IRunLauncher>();
        return await launcher.LaunchAsync(projectId, null, ticket, cancellationToken);
    }

    private async Task<int> RunCountAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        await using var db = server.CreateOrchestrationDb();
        return await db.Runs.IgnoreQueryFilters().CountAsync(run => run.ProjectId == projectId, cancellationToken);
    }

    private static IncomingTicket NewTicket(ProjectId projectId)
    {
        return IncomingTicket.Create(
            projectId,
            TicketProvider.Native,
            "ws9-fixture-" + Guid.NewGuid().ToString("N"),
            "WS9 idempotency fixture",
            "body",
            "tester",
            url: string.Empty,
            projectKey: null,
            labels: [],
            InboundTicketKind.Issue,
            DateTimeOffset.UtcNow);
    }
}
