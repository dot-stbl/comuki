using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Inbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Comuki.Host.Intake;

/// <summary>
/// Host-side run launcher (the IRunLauncher port): one ticket → one run
/// with one queued work item — reusing the engine's domain factories
/// exactly like <c>ChatRunStarter</c>. Scoped — one orchestration
/// context per apply; the intake module never references the engine.
/// Profile routing goes through <see cref="IIntakeProfileRouter"/>: a
/// per-connection <c>profileKey</c> override wins, PR-kind tickets
/// default to <c>pr-review</c>, issues to <c>defaults.ProfileKey</c>.
/// <para>
/// WS9 (issue #87) admission idempotency contract: this launcher is
/// safe to call twice (sequentially or concurrently) on tickets
/// carrying the same <see cref="IncomingTicket.Id"/> — at most one Run
/// is created, and a losing / retried call observes the winner's Run
/// id rather than throwing or duplicating. The mechanism is an inbox
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> claim whose winning insert,
/// the new Run and the first WorkItem are committed in the same
/// Postgres transaction so the loser's lookup never races a missing
/// row. This is an Orchestration-side guarantee only — Intake's own
/// delivery-id / active-ticket locks remain the upstream defence.
/// </para>
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="inbox">WS6 dedupe ledger — guards the admission call.</param>
/// <param name="profileRouter">Profile-key resolver (PRs vs, / issues).</param>
/// <param name="defaults">Claim labels for intake-created items.</param>
/// <param name="clock">Time source for domain stamps.</param>
public sealed class IntakeRunLauncher(
    OrchestrationDbContext db,
    IInbox inbox,
    IIntakeProfileRouter profileRouter,
    IOptions<IntakeWorkerDefaults> defaults,
    TimeProvider clock) : IRunLauncher
{
    /// <summary>
    /// Launches the run for a ticket; returns the run id (the new run's
    /// id when this call wins the dedupe, the winner's existing run id
    /// when another in-flight or already-committed claim is on the same
    /// admission identity — never throws and never creates a duplicate).
    /// </summary>
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
        var messageId = $"admission-{ticket.Id.Value:D}";

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!await inbox.TryClaimAsync(messageId, cancellationToken))
        {
            // IgnoreQueryFilters is deliberate: messageId is derived from
            // the ticket's own id (above), never from user input, so this
            // is an internal invariant lookup, not a subject-scoped read —
            // the ambient request scope must not hide the winner's run from
            // a legitimately losing/retried caller in the same scope.
            var existingRunId = await AdmissionLookup.FindByMessageIdAsync(db, messageId, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);

            return existingRunId ?? throw new InvalidOperationException(
                $"admission message id '{messageId}' was claimed but no run is bound to it");
        }

        var run = Run.Create(projectId, now, messageId);
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // Defense in depth for the "never throws" contract in the class
            // doc above: the inbox claim already makes this branch
            // unreachable in theory (the PK on message_id serializes
            // concurrent claims to one winner before either transaction
            // reaches this SaveChangesAsync), but if
            // ux_runs_admission_message_id ever fires anyway, treat it
            // exactly like a lost inbox race instead of surfacing a
            // duplicate-key error to the caller.
            db.Entry(run).State = EntityState.Detached;
            db.Entry(workItem).State = EntityState.Detached;
            var existingRunId = await AdmissionLookup.FindByMessageIdAsync(db, messageId, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);

            return existingRunId ?? throw new InvalidOperationException(
                $"admission message id '{messageId}' hit a unique-key race but no run is bound to it");
        }

        await transaction.CommitAsync(cancellationToken);
        return run.Id;
    }
}

/// <summary>Looks up the run bound to an already-claimed admission message id (the dedupe race's winner).</summary>
file static class AdmissionLookup
{
    public static async Task<RunId?> FindByMessageIdAsync(OrchestrationDbContext db, string messageId, CancellationToken cancellationToken)
    {
        var existingRun = await db.Runs
            .IgnoreQueryFilters()
            .Where(run => run.AdmissionMessageId == messageId)
            .Select(static run => new { run.Id })
            .SingleOrDefaultAsync(cancellationToken);

        return existingRun?.Id;
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
