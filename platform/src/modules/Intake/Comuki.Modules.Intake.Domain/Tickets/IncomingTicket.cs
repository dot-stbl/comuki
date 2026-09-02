using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Domain.Tickets;

/// <summary>
/// One external issue seen by intake — the dedupe view of everything the
/// webhooks and the catalog fetched. <see cref="ExternalId"/> is the
/// fully-qualified tracker-side identifier (e.g. <c>dot-stbl/comuki#481</c>,
/// <c>COMUKI-481</c>) so one project can bind several repos/queues without
/// colliding. The one-live-run-per-issue lock is the partial unique index
/// over <c>(project_id, provider, external_id)</c> restricted to the active
/// statuses — see <see cref="IntakeTicketStatus"/>.
/// </summary>
public sealed class IncomingTicket
{
    internal IncomingTicket()
    {
    }

    /// <summary>Strong-typed ticket id (UUIDv7).</summary>
    public IncomingTicketId Id { get; private set; }

    /// <summary>Project the ticket was admitted into.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Source tracker the ticket came from.</summary>
    public TicketProvider Provider { get; private set; }

    /// <summary>Fully-qualified external identifier, unique within the source.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>Issue title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Issue body / description.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Author login / display name on the source tracker.</summary>
    public string Author { get; private set; } = string.Empty;

    /// <summary>Browsable issue URL for the run view and sync-back comments.</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// The tracker-side grouping key used by the admission filter's
    /// project list — repo full name (GitHub/GitLab), queue key
    /// (Yandex Tracker) or project key (Jira); null when the source has
    /// no such notion.
    /// </summary>
    public string? ProjectKey { get; private set; }

    /// <summary>Issue labels at the time of the delivery.</summary>
    public string[] Labels { get; private set; } = [];

    /// <summary>Current lifecycle status; mutated only via the Mark* methods.</summary>
    public IntakeTicketStatus Status { get; private set; }

    /// <summary>The run launched for this ticket; null until claimed.</summary>
    public RunId? RunId { get; private set; }

    /// <summary>When the ticket was first seen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last status change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a pending ticket — the only entry status.</summary>
    /// <param name="projectId"></param>
    /// <param name="provider"></param>
    /// <param name="externalId"></param>
    /// <param name="title"></param>
    /// <param name="body"></param>
    /// <param name="author"></param>
    /// <param name="url"></param>
    /// <param name="projectKey"></param>
    /// <param name="labels"></param>
    /// <param name="now"></param>
    public static IncomingTicket Create(
        ProjectId projectId,
        TicketProvider provider,
        string externalId,
        string title,
        string body,
        string author,
        string url,
        string? projectKey,
        IReadOnlyList<string> labels,
        DateTimeOffset now)
    {
        return new IncomingTicket
        {
            Id = IncomingTicketId.New(),
            ProjectId = projectId,
            Provider = provider,
            ExternalId = externalId,
            Title = title,
            Body = body,
            Author = author,
            Url = url,
            ProjectKey = projectKey,
            Labels = [.. labels],
            Status = IntakeTicketStatus.Pending,
            RunId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Claims the ticket for a run; legal only from <see cref="IntakeTicketStatus.Pending"/>.</summary>
    /// <param name="runId"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException">The ticket is not pending.</exception>
    public void MarkClaimed(RunId runId, DateTimeOffset now)
    {
        if (Status is not IntakeTicketStatus.Pending)
        {
            throw new InvalidOperationException($"ticket {Id} cannot be claimed from status {Status}");
        }

        RunId = runId;
        Status = IntakeTicketStatus.Claimed;
        UpdatedAt = now;
    }

    /// <summary>
    /// Releases the lock after the run reached a terminal status; legal
    /// only from <see cref="IntakeTicketStatus.Claimed"/>.
    /// </summary>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException">The ticket has no live claim.</exception>
    public void MarkDone(DateTimeOffset now)
    {
        if (Status is not IntakeTicketStatus.Claimed)
        {
            throw new InvalidOperationException($"ticket {Id} cannot be released from status {Status}");
        }

        Status = IntakeTicketStatus.Done;
        UpdatedAt = now;
    }

    /// <summary>Marks a pending ticket filtered-out; never conflicts with the active lock.</summary>
    /// <param name="now"></param>
    public void MarkDismissed(DateTimeOffset now)
    {
        Status = IntakeTicketStatus.Dismissed;
        UpdatedAt = now;
    }
}
