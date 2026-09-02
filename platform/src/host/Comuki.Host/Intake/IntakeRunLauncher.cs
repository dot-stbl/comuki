using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;
namespace Comuki.Host.Intake;

/// <summary>
/// Host-side run launcher (the IRunLauncher port): one ticket → one run
/// with one queued work item — reusing the engine's domain factories
/// exactly like <c>ChatRunStarter</c>. Scoped — one orchestration
/// context per apply; the intake module never references the engine.
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="defaults">Claim labels for intake-created items.</param>
/// <param name="clock">Time source for domain stamps.</param>
public sealed class IntakeRunLauncher(
    OrchestrationDbContext db,
    IOptions<IntakeWorkerDefaults> defaults,
    TimeProvider clock) : IRunLauncher
{
    /// <summary>Launches the run for a ticket; returns the created run id.</summary>
    /// <param name="projectId"></param>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    public async Task<RunId> LaunchAsync(ProjectId projectId, IncomingTicket ticket, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var run = Run.Create(projectId, now);
        var workItem = WorkItem.Create(
            run.Id,
            defaults.Value.ProfileKey,
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
            new IntakeItemGoal(goal, TicketProviderKeys.Key(ticket.Provider), ticket.ExternalId),
            JsonSerializerOptions.Web);
    }
}

/// <summary>Worker brief payload — mirrors the queue integration seeds.</summary>
/// <param name="Goal">The worker goal (ticket title + body).</param>
/// <param name="Source">Kebab-case provider key.</param>
/// <param name="ExternalId">Fully-qualified external issue id.</param>
internal sealed record IntakeItemGoal(string Goal, string Source, string ExternalId);
