using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Integrations.Domain.Items;

/// <summary>
/// One external issue seen by integrations — the dedupe view of everything the
/// webhooks and the catalog fetched. <see cref="ExternalId"/> is the
/// fully-qualified tracker-side identifier (e.g. <c>dot-stbl/comuki#481</c>,
/// <c>COMUKI-481</c>) so one project can bind several repos/queues without
/// colliding. The one-live-run-per-issue lock is the partial unique index
/// over <c>(project_id, provider, external_id)</c> restricted to the active
/// statuses — see <see cref="InboundItemStatus"/>.
/// </summary>
public sealed class InboundItem
{
    internal InboundItem()
    {
    }

    /// <summary>Strong-typed inbound item id (UUIDv7).</summary>
    public InboundItemId Id { get; private set; }

    /// <summary>Project the inbound item was admitted into.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Source tracker the inbound item came from.</summary>
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

    /// <summary>
    /// The connection the inbound item arrived through — the sync-back
    /// routing target; null for native items (no external tracker).
    /// </summary>
    public SourceConnectionId? ConnectionId { get; private set; }

    /// <summary>
    /// What kind of tracker-side object this inbound item represents. The
    /// webhook mapper sets it; <see cref="InboundItemKind.Issue"/> for
    /// regular issues, <see cref="InboundItemKind.PullRequest"/> for
    /// pull-request / merge-request events (inbound review surface).
    /// Drives the profile router's default choice.
    /// </summary>
    public InboundItemKind Kind { get; private set; }

    /// <summary>Current lifecycle status; mutated only via the Mark* methods.</summary>
    public InboundItemStatus Status { get; private set; }

    /// <summary>The run launched for this inbound item; null until claimed.</summary>
    public RunId? RunId { get; private set; }

    /// <summary>When the inbound item was first seen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last status change timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a pending inbound item — the only entry status.</summary>
    /// <param name="projectId"></param>
    /// <param name="provider"></param>
    /// <param name="externalId"></param>
    /// <param name="title"></param>
    /// <param name="body"></param>
    /// <param name="author"></param>
    /// <param name="url"></param>
    /// <param name="projectKey"></param>
    /// <param name="labels"></param>
    /// <param name="kind">Issue (default) or pull request.</param>
    /// <param name="now"></param>
    public static InboundItem Create(
        ProjectId projectId,
        TicketProvider provider,
        string externalId,
        string title,
        string body,
        string author,
        string url,
        string? projectKey,
        IReadOnlyList<string> labels,
        InboundItemKind kind,
        DateTimeOffset now)
    {
        return new InboundItem
        {
            Id = InboundItemId.New(),
            ProjectId = projectId,
            Provider = provider,
            ExternalId = externalId,
            Title = title,
            Body = body,
            Author = author,
            Url = url,
            ProjectKey = projectKey,
            Labels = [.. labels],
            Kind = kind,
            Status = InboundItemStatus.Pending,
            RunId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Binds the inbound item to the connection it arrived through (the
    /// sync-back routing target). Set once by the webhook pipeline
    /// before the inbound item is stored; native items stay unbound.
    /// </summary>
    public void BindConnection(SourceConnectionId connectionId)
    {
        ConnectionId = connectionId;
    }

    /// <summary>Claims the inbound item for a run; legal only from <see cref="InboundItemStatus.Pending"/>.</summary>
    /// <param name="runId"></param>
    /// <param name="now"></param>
    /// <exception cref="InvalidOperationException">The inbound item is not pending.</exception>
    public void MarkClaimed(RunId runId, DateTimeOffset now)
    {
        if (Status is not InboundItemStatus.Pending)
        {
            throw new InvalidOperationException($"inbound item {Id} cannot be claimed from status {Status}");
        }

        RunId = runId;
        Status = InboundItemStatus.Claimed;
        UpdatedAt = now;
    }

    /// <summary>
    /// Releases the lock after the run reached a terminal status; legal
    /// only from <see cref="InboundItemStatus.Claimed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The inbound item has no live claim.</exception>
    public void MarkDone(DateTimeOffset now)
    {
        if (Status is not InboundItemStatus.Claimed)
        {
            throw new InvalidOperationException($"inbound item {Id} cannot be released from status {Status}");
        }

        Status = InboundItemStatus.Done;
        UpdatedAt = now;
    }

    /// <summary>Marks a pending inbound item filtered-out; never conflicts with the active lock.</summary>
    public void MarkDismissed(DateTimeOffset now)
    {
        Status = InboundItemStatus.Dismissed;
        UpdatedAt = now;
    }
}
