using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;
namespace Comuki.Host.Intake;

/// <summary>
/// Host-side run launcher (the IRunLauncher port): one ticket → one run
/// with one queued work item — reusing the engine's domain factories
/// exactly like <c>ChatRunStarter</c>. Scoped — one orchestration
/// context per apply; the intake module never references the engine.
/// Profile routing goes through <see cref="IIntakeProfileRouter"/>: a
/// per-connection <c>profileKey</c> override wins, PR-kind tickets
/// default to <c>pr-review</c>, issues to <c>defaults.ProfileKey</c>.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="profileRouter">Profile-key resolver (PRs vs, / issues).</param>
/// <param name="defaults">Claim labels for intake-created items.</param>
/// <param name="clock">Time source for domain stamps.</param>
public sealed class IntakeRunLauncher(
    OrchestrationDbContext db,
    IIntakeProfileRouter profileRouter,
    IOptions<IntakeWorkerDefaults> defaults,
    TimeProvider clock) : IRunLauncher
{
    /// <summary>Launches the run for a ticket; returns the created run id.</summary>
    /// <param name="projectId"></param>
    /// <param name="connection">The source connection the ticket arrived through.</param>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    public async Task<RunId> LaunchAsync(
        ProjectId projectId,
        SourceConnection? connection,
        IncomingTicket ticket,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = Run.Create(projectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            profileRouter.ResolveProfileKey(connection, ticket),
            defaults.Value.Image,
            defaults.Value.ProfilesRef,
            IntakeItemBrief.ToJson(ticket),
            WorkItemStatus.Queued,
            now);

        db.Runs.Add(run);
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
}

/// <summary>Ticket → worker brief jsonb (the <c>goal</c> shape the worker runtime reads).</summary>
file static class IntakeItemBrief
{
    public static string ToJson(IncomingTicket ticket)
    {
        var goal = ticket.Body.Length == 0
            ? ticket.Title
            : ticket.Title + "\n\n" + ticket.Body;

        return JsonSerializer.Serialize(
            new IntakeItemGoal(goal, TicketProviderKeys.Key(ticket.Provider), ticket.ExternalId, ticket.Kind),
            JsonSerializerOptions.Web);
    }
}

/// <summary>Worker brief payload — mirrors the queue integration seeds.</summary>
/// <param name="Goal">The worker goal (ticket title + body).</param>
/// <param name="Source">Kebab-case provider key.</param>
/// <param name="ExternalId">Fully-qualified external issue id.</param>
/// <param name="Kind">Issue or pull request — drives the worker skill choice.</param>
internal sealed record IntakeItemGoal(string Goal, string Source, string ExternalId, InboundTicketKind Kind);
