using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Inbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Projects;
using Comuki.Engine.Compute.Options;
using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Shared.Bootstrap.Versioning;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Comuki.Host.Integration;

/// <summary>
/// Host-side run launcher (the IRunLauncher port): one ticket → one run
/// with one queued work item — reusing the engine's domain factories
/// exactly like <c>ChatRunStarter</c>. Scoped — one orchestration
/// context per apply; the integrations module never references the engine.
/// Profile routing goes through <see cref="IIntegrationProfileRouter"/>: a
/// per-connection <c>profileKey</c> override wins, PR-kind tickets
/// default to <c>pr-review</c>, issues to <c>defaults.ProfileKey</c>.
/// <para>
/// WS9 (issue #87) admission idempotency contract: this launcher is
/// safe to call twice (sequentially or concurrently) on tickets
/// carrying the same <see cref="InboundItem.Id"/> — at most one Run
/// is created, and a losing / retried call observes the winner's Run
/// id rather than throwing or duplicating. The mechanism is an inbox
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> claim whose winning insert,
/// the new Run and the first WorkItem are committed in the same
/// Postgres transaction so the loser's lookup never races a missing
/// row. This is an Orchestration-side guarantee only — Integrations' own
/// delivery-id / active-ticket locks remain the upstream defence.
/// </para>
/// </summary>
/// <param name="db">Orchestration context of the current scope.</param>
/// <param name="inbox">WS6 dedupe ledger — guards the admission call.</param>
/// <param name="profileRouter">Profile-key resolver (PRs vs. issues).</param>
/// <param name="defaults">Claim labels for integrations-created items.</param>
/// <param name="buildInformation">Build identity — pins the item image to the running version (release contract).</param>
/// <param name="clock">Time source for domain stamps.</param>
/// <param name="projects">Projects module port — stamps <c>Project.EnvClass</c> onto the work item (task 3.1).</param>
public sealed class IntegrationRunLauncher(
    OrchestrationDbContext db,
    IInbox inbox,
    IIntegrationProfileRouter profileRouter,
    IOptions<IntegrationWorkerDefaults> defaults,
    ComukiBuildInformation buildInformation,
    TimeProvider clock,
    IProjectStore projects) : IRunLauncher
{
    /// <summary>
    /// Launches the run for a ticket; returns the run id (the new run's
    /// id when this call wins the dedupe, the winner's existing run id
    /// when another in-flight or already-committed claim is on the same
    /// admission identity — never throws and never creates a duplicate).
    /// </summary>
    /// <param name="projectId">Project the admitted inbound item belongs to; becomes the run's owner.</param>
    /// <param name="connection">The source connection the ticket arrived through, or null for native (manual) items.</param>
    /// <param name="ticket">The admitted inbound item; carries the external id, title, URL, source, and provider reference.</param>
    /// <param name="cancellationToken">Cancellation observed across the inbox claim, the run insert, and the work-item materialisation.</param>
    public async Task<RunId> LaunchAsync(
        ProjectId projectId,
        SourceConnection? connection,
        InboundItem ticket,
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
        // Claim matching compares the item's image with the worker's
        // labels for equality — the supervisor pins its spawn through
        // WorkerImagePinning, so the item side must resolve through the
        // same function or no worker ever matches (release contract,
        // see WorkerImagePinning).
        var image = WorkerImagePinning.Resolve(defaults.Value.Image, buildInformation);
        var workItem = WorkItem.Create(
            run.Id,
            profileRouter.ResolveProfileKey(connection, ticket),
            image,
            await EnvClassResolver.ResolveAsync(projects, projectId, "integrations admission", cancellationToken),
            defaults.Value.ProfilesRef,
            InboundItemBrief.ToJson(ticket),
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

/// <summary>Inbound item → worker brief jsonb (the <c>goal</c> shape the worker runtime reads).</summary>
file static class InboundItemBrief
{
    public static string ToJson(InboundItem ticket)
    {
        var goal = ticket.Body.Length == 0
            ? ticket.Title
            : ticket.Title + "\n\n" + ticket.Body;

        return JsonSerializer.Serialize(
            new InboundItemGoal(goal, TicketProviderKeys.Key(ticket.Provider), ticket.ExternalId, ticket.Kind),
            JsonSerializerOptions.Web);
    }
}

/// <summary>Worker brief payload — mirrors the queue integration seeds.</summary>
/// <param name="Goal">The worker goal (inbound item title + body).</param>
/// <param name="Source">Kebab-case provider key.</param>
/// <param name="ExternalId">Fully-qualified external issue id.</param>
/// <param name="Kind">Issue or pull request — drives the worker skill choice.</param>
internal sealed record InboundItemGoal(string Goal, string Source, string ExternalId, InboundItemKind Kind);
