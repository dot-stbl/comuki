import type { ClaimTicketRequest } from "@/shared/api/_generated/types/ClaimTicketRequest"
import type { CreateNativeTicketRequest } from "@/shared/api/_generated/types/CreateNativeTicketRequest"
import type { IntakeTicketView } from "@/shared/api/_generated/types/IntakeTicketView"
import type { WebhookAcceptedResponse } from "@/shared/api/_generated/types/WebhookAcceptedResponse"

import type {
  CatalogProjection,
  ClaimTicketInput,
  CreateNativeTicketInput,
  Ticket,
  TicketStatus,
} from "@/domains/inbox/model/types"
import type {
  SeedNativeTicket,
} from "@/shared/api/mock/sources.seed"

/* ---------------------------------------------------------------------------
 * Wire → domain mappers.
 *
 * The host returns the kubb-generated `IntakeTicketView`. It is the same
 * shape across the local pending list (`GET /api/v1/inbox`), the external
 * catalog browse (`GET /api/v1/inbox/catalog`) and the claim response
 * (`POST /api/v1/inbox/claim`). One mapper turns the wire row into a
 * domain `Ticket`; the rest compose.
 *
 * The wire carries `status: string` (kubb's loose typing of an enum whose
 * `ToString()` is what the host hands back). The mapper normalises the four
 * known lifecycle values and defaults anything else to `"pending"` so a
 * partial backend upgrade degrades the row, not the screen.
 *
 * `kind` is not on the wire yet (#27 added it server-side but the read view
 * still ships without it). Defaulted to `"issue"`; one mapper line is the
 * change when the discriminator lands.
 * ------------------------------------------------------------------------- */

/** Closed set of statuses the wire row may carry; see <see cref="TicketStatus"/>. */
const KNOWN_STATUSES = new Set<TicketStatus>([
  "pending",
  "claimed",
  "done",
  "dismissed",
])

/**
 * Normalise the wire `status` string to the domain union.
 *
 * Unknown values fall through to `"pending"` rather than throwing — the host
 * may have rolled out a status the FE has not yet taught the mapper. The
 * row still renders, with the wrong status; the screen flags this case via
 * the inbox's own "unknown state" affordance, not a crash.
 */
export function normalizeTicketStatus(value: string): TicketStatus {
  if (KNOWN_STATUSES.has(value as TicketStatus)) {
    return value as TicketStatus
  }
  return "pending"
}

/**
 * Wire row → domain ticket.
 *
 * The wire `id` is a UUID; the seed ticket ids in mock mode are short
 * human-readable strings (`nt_4120`). The mock-side mapper hands back a
 * UUID-shaped id derived from the seed id, so a session-stable mock and a
 * real backend share the same domain type without a branch in callers.
 *
 * `kind` defaults to `"issue"` — see the mapper header for why.
 */
export function mapIntakeTicketViewToTicket(view: IntakeTicketView): Ticket {
  return {
    id: view.id,
    projectId: view.projectId,
    source: view.source,
    externalId: view.externalId,
    title: view.title,
    url: view.url,
    status: normalizeTicketStatus(view.status),
    runId: view.runId,
    createdAt: view.createdAt,
    kind: "issue",
  }
}

/**
 * Wire list (`GET /api/v1/inbox`) → list of domain tickets.
 *
 * The host returns the rows as a bare JSON array — no envelope. The same
 * mapper handles the claim response's single row (call sites are
 * <c>mapIntakeTicketViewToTicket</c> directly when the response is one row).
 */
export function mapInboxToTickets(
  page: IntakeTicketView[]
): Ticket[] {
  return page.map(mapIntakeTicketViewToTicket)
}

/**
 * Wire catalog page (`GET /api/v1/inbox/catalog?connectionId=…`) → domain
 * projection.
 *
 * The catalog endpoint takes the connection id as a query param but does
 * not echo it back on the row, so the projection carries it itself. The
 * screen's `useInboxCatalogQuery` already knows the connection id; carrying
 * it on the result keeps the call site symmetric with the inbox list (which
 * is implicitly "no specific connection").
 */
export function mapInboxCatalogToConnections(
  page: IntakeTicketView[],
  connectionId: string
): CatalogProjection {
  return {
    connectionId,
    items: page.map(mapIntakeTicketViewToTicket),
  }
}

/* ---------------------------------------------------------------------------
 * Domain → wire mappers.
 *
 * Two output shapes today: <c>ClaimTicketRequest</c> (single id) and
 * <c>CreateNativeTicketRequest</c> (project id, title, optional body /
 * externalId / author). The wire expects the host to mint the optional
 * fields when they're empty, so the mapper passes through `undefined`
 * verbatim — kubb's zod schema accepts both empty and absent as "omit".
 * ------------------------------------------------------------------------- */

/**
 * Domain claim input → wire request body.
 *
 * The host treats this as exactly-once; the FE mapper only owns the field
 * shape, not the idempotency contract.
 */
export function mapClaimTicketInputToClaimRequest(
  input: ClaimTicketInput
): ClaimTicketRequest {
  return { ticketId: input.ticketId }
}

/**
 * Domain native ticket input → wire request body.
 *
 * Optional fields stay optional; the host fills in a generated externalId
 * and stamps the session user as author when the operator leaves them
 * empty. The mapper never invents a value the caller did not supply.
 */
export function mapNativeTicketInputToCreateRequest(
  input: CreateNativeTicketInput
): CreateNativeTicketRequest {
  return {
    projectId: input.projectId,
    title: input.title,
    body: input.body,
    externalId: input.externalId,
    author: input.author,
  }
}

/* ---------------------------------------------------------------------------
 * Mock-side mappers.
 *
 * The mock store holds `SeedNativeTicket` (the sources-domain shape), not
 * the wire shape. These two mappers bridge the seed to the domain type the
 * inbox queries hand back, so mock mode and real mode return the same shape
 * and callers never branch on the result.
 *
 * `status` is derived from `straightToWork` because the seed carries the
 * run-side fact and not the explicit enum: a ticket with `straightToWork`
 * was claimed the moment it was filed (the operator asked for a run to be
 * opened at the same time). `runId` is synthesised for claimed rows and
 * null for the rest, mirroring the wire shape exactly.
 *
 * `externalId` reuses the seed id — native tickets do not carry an external
 * tracker id; the FE surfaces a stable identifier that the operator can
 * recognise in the inbox list.
 * ------------------------------------------------------------------------- */

const MOCK_RUN_ID =
  "00000000-0000-0000-0000-runstub000001"

/** Mock-side ticket → domain ticket (synthesises UUID id and run id). */
export function mapSeedTicketToTicket(
  ticket: SeedNativeTicket
): Ticket {
  const claimed = ticket.straightToWork
  return {
    id: mockTicketUuid(ticket.id),
    projectId: ticket.projectId,
    source: "native",
    externalId: ticket.id,
    title: ticket.title,
    url: mockTicketUrl(ticket.id),
    status: claimed ? "claimed" : "pending",
    runId: claimed ? MOCK_RUN_ID : null,
    createdAt: mockTicketCreatedAt(ticket.createdAt),
    kind: "issue",
  }
}

/**
 * ISO-8601 normalisation for the mock side.
 *
 * The seed uses friendly labels ("2026-08-28", "just now"). Real backend
 * timestamps are ISO-8601 with an offset. The mapper passes through any
 * value that already looks like an ISO-8601 string and falls back to the
 * current instant for "just now" / unknown — the screen renders timestamps
 * via the same formatter, which accepts both.
 */
function mockTicketCreatedAt(seedCreatedAt: string): string {
  if (seedCreatedAt === "just now") {
    return new Date().toISOString()
  }
  return seedCreatedAt
}

/**
 * Mock deep link into the (imagined) tracker view for a native ticket.
 * Stable per seed id so the same row keeps its link across refetches.
 */
function mockTicketUrl(seedId: string): string {
  return `https://comuki.local/inbox/${seedId}`
}

/**
 * Stable UUID-shaped mock id for a seed ticket.
 *
 * Derived from the seed id's suffix so the same mock ticket returns the
 * same UUID across reloads. Kubb's zod schema validates `id` as a UUID,
 * and the screen's optimistic-write path keys off it.
 */
function mockTicketUuid(seedId: string): string {
  const tail = seedId.padEnd(12, "0").slice(0, 12)
  return `00000000-0000-0000-0000-${tail}`
}

/* ---------------------------------------------------------------------------
 * Inbound webhook — the only call whose mock answer is not a domain type.
 *
 * The webhook surface (`POST /api/hooks/{provider}/{key}`) is anonymous:
 * the host identifies the connection by the path, not by a session. The FE
 * never invokes this in normal operation — the dashboard exposes webhook
 * URLs for the operator to register with their tracker, not for the SPA to
 * hit. The kubb hook exists so a storybook / dev:mock can render the
 * "send a test webhook" form.
 *
 * The hook's only return is a kubb `WebhookAcceptedResponse`; we re-export
 * the type so callers do not reach into the generated tree for a one-line
 * `outcome`/`detail` pair.
 * ------------------------------------------------------------------------- */

/** Re-export the kubb response shape under a friendlier name. */
export type WebhookOutcome = WebhookAcceptedResponse
