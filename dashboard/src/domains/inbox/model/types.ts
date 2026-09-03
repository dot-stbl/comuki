/**
 * The inbox — the place work lands before it is claimed into a run.
 *
 * A ticket is a single row, regardless of which side it came from: a webhook
 * parked it on the local pending list (the inbox proper) or a person typed it
 * straight into the product's own intake (the native surface). Both speak the
 * same `IntakeTicketView` on the wire; the catalog browse of an external
 * connection is the same shape again, viewed from the other side.
 *
 * The lifecycle the host returns is the closed set of <c>IntakeTicketStatus</c>
 * — `Pending | Claimed | Done | Dismissed`. The wire carries the enum's
 * <c>ToString()</c> rather than the integer, so we type the status as the
 * string union that stringifies to. A value the host has not taught us is
 * mapped to `"pending"` so a partial upgrade of the backend does not crash
 * the dashboard.
 */

/**
 * Lifecycle of an intake ticket.
 *
 * Mirrors the backend <c>IntakeTicketStatus</c>; the wire carries the
 * <c>ToString()</c> of that enum. The domain treats unknown statuses as
 * <c>"pending"</c> rather than throwing — a partial backend rollout should
 * degrade the row, not the screen.
 */
export type TicketStatus = "pending" | "claimed" | "done" | "dismissed"

/**
 * What a ticket represents on the tracker side.
 *
 * Added in #27 (inbound PR-review). The wire row carries no kind field yet —
 * a real connection's `IntakeTicketView` is still the four external
 * trackers + native, all of which look like issues until PR support lands on
 * the read side. The mapper defaults to <c>"issue"</c>; once the backend
 * surfaces the discriminator, a single mapper line is what changes.
 */
export type TicketKind = "issue" | "pull-request"

/**
 * One row of the inbox, list or detail.
 *
 * The wire row (<c>IntakeTicketView</c>) is intentionally sparse — the host's
 * read model keeps a ticket small. The dashboard adds nothing of its own:
 * <c>kind</c> defaults to <c>"issue"</c> (not yet on the wire), and every
 * other field is the host's own value, verbatim.
 */
export interface Ticket {
  /** The host's ticket id, UUID. */
  id: string
  /** The project this ticket belongs to, by id. */
  projectId: string
  /**
   * Kebab-case provider key (<c>"github" | "gitlab" | "yandex-tracker" | "jira"
   * | "native"</c>). The host normalises to the kebab form so the wire row
   * matches the webhook route segment, not the enum's PascalCase spelling.
   */
  source: string
  /** The provider's own id for the issue (issue number, MR iid, etc.). */
  externalId: string
  /** The title the tracker has — what a person sees in their queue. */
  title: string
  /** Deep link back to the tracker (the issue page). */
  url: string
  /** Lifecycle status. See <see cref="TicketStatus" />. */
  status: TicketStatus
  /**
   * The run this ticket became, once claimed. <c>null</c> while still
   * pending; the screen hides "open run" affordances on <c>null</c>.
   */
  runId: string | null
  /** Ticket's intake time, ISO-8601. */
  createdAt: string
  /**
   * Tracker-side object kind (issue vs PR). The wire does not yet carry the
   * discriminator — defaulted to <c>"issue"</c>; see <see cref="TicketKind" />.
   */
  kind: TicketKind
}

/**
 * One page of the external catalog browse — what's at the other end of
 * <c>connectionId</c>.
 *
 * The catalog endpoint takes a connection id and a 1-based page number and
 * returns the same <c>IntakeTicketView</c> shape the local inbox returns.
 * The projection carries the connection id because the response itself does
 * not echo it, and the screen has to know whose queue it is showing.
 */
export interface CatalogProjection {
  /** The connection whose catalog this page is from. */
  connectionId: string
  /** The page of tickets the host handed back. */
  items: Ticket[]
}

/**
 * Filters the inbox list accepts.
 *
 * The wire endpoint takes exactly one filter — a project id. The screen will
 * pass <c>undefined</c> when the operator wants the whole platform's queue.
 */
export interface InboxFilters {
  /** Restrict to one project, or undefined for the platform-wide view. */
  projectId?: string
}

/**
 * The body of a manual claim — the only field is which ticket.
 *
 * Mirrors the kubb-generated <c>ClaimTicketRequest</c>; the mapper below
 * turns this into it. The host treats this as exactly-once: a repeat claim
 * answers 409, the screen's error boundary reads that.
 */
export interface ClaimTicketInput {
  ticketId: string
}

/**
 * The body of the native intake — what a person typed here rather than in a
 * tracker.
 *
 * Mirrors the kubb-generated <c>CreateNativeTicketRequest</c>. <c>body</c>,
 * <c>externalId</c> and <c>author</c> are optional on the wire (the host
 * fills in a generated externalId and stamps the session user as author
 * when they're empty).
 */
export interface CreateNativeTicketInput {
  /** The project this ticket (and its run) will live under. */
  projectId: string
  title: string
  /** Free-text body. Optional on the wire. */
  body?: string
  /**
   * Caller-supplied dedupe id; the host generates one when empty.
   * Useful when the operator is migrating a backlog from another tool.
   */
  externalId?: string
  /** Author label. Optional on the wire; the host defaults to the session. */
  author?: string
}
