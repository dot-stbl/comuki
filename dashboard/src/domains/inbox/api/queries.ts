import { useQuery } from "@tanstack/react-query"

import {
  mapInboxCatalogToConnections,
  mapInboxToTickets,
  mapSeedTicketToTicket,
} from "@/domains/inbox/api/mappers"
import type {
  CatalogProjection,
  InboxFilters,
  Ticket,
} from "@/domains/inbox/model/types"
import { getApiV1Inbox } from "@/shared/api/_generated/clients/getApiV1Inbox"
import { getApiV1InboxCatalog } from "@/shared/api/_generated/clients/getApiV1InboxCatalog"
import { findSeedInboxTicket, listSeedInboxTickets } from "@/shared/api/mock/sources.store"
import { env } from "@/shared/config/env"

/* ---------------------------------------------------------------------------
 * Inbox queries — three reads, mock-first.
 *
 * `VITE_USE_MOCK=true` keeps the seed-backed path the operator's local flow
 * already relies on. `VITE_USE_MOCK=false` switches every read over to
 * kubb-generated clients that route through `kubb-client.ts` — itself
 * gated on `VITE_API_BASE_URL` being set. Without the env var the kubb
 * transport throws a single, readable message at first call rather than
 * pinging localhost and getting a Vite-served 404.
 *
 * Three endpoints are wired today:
 *
 *   - `GET /api/v1/inbox`              → pending list (newest first)
 *   - `GET /api/v1/inbox/catalog`      → one page of a connection's external
 *                                        issue catalog (browse then take)
 *   - (no detail endpoint)             → a single ticket is pulled out of the
 *                                        cached list. The host's `InboxController`
 *                                        does not expose `/api/v1/inbox/{id}`;
 *                                        the list-page approach is intentional
 *                                        and a single function changes when
 *                                        the detail endpoint lands.
 *
 * Each query exports a stable query key so callers can invalidate it from
 * the mutation hooks without reaching for the function reference.
 * ------------------------------------------------------------------------- */

export const inboxQueryKey = ["inbox"] as const
export const inboxCatalogQueryKey = (connectionId: string) =>
  ["inbox", "catalog", connectionId] as const

/**
 * The pending inbox.
 *
 * `InboxFilters.projectId` is the only filter the host exposes today. The
 * mapper carries the rest of the row shape; the screen picks the columns it
 * wants from the resulting `Ticket[]`.
 *
 * Mock mode reads the same `state.tickets` the sources screen renders
 * (`SeedNativeTicket`) and synthesises a UUID + deep-link URL on the way out,
 * so callers never branch on the result type. Mock claim results are
 * recorded in the sources store too; the next refetch reflects them.
 */
async function listInbox(filters: InboxFilters): Promise<Ticket[]> {
  if (env.useMock) {
    return listSeedInboxTickets(filters.projectId).map(mapSeedTicketToTicket)
  }
  const page = await getApiV1Inbox({
    projectId: filters.projectId,
  })
  return mapInboxToTickets(page)
}

/**
 * Browse one page of an external connection's catalog.
 *
 * The host's `InboxController.FetchCatalogAsync` requires a `connectionId`
 * and a 1-based `page` — defaults to `1`. Mock mode returns an empty
 * projection: there is no fake tracker to browse, and the screen's catalog
 * UI already knows "empty" is a valid answer.
 */
async function getInboxCatalog(
  connectionId: string,
  page: number = 1
): Promise<CatalogProjection> {
  if (env.useMock) {
    return mapInboxCatalogToConnections([], connectionId)
  }
  const rows = await getApiV1InboxCatalog({
    connectionId,
    page,
  })
  return mapInboxCatalogToConnections(rows, connectionId)
}

/**
 * Fetch one ticket by id.
 *
 * **Limitation: there is no detail endpoint on the wire today.** The host's
 * `InboxController` only exposes the list and the catalog; the only way to
 * resolve a single ticket is to pull it out of the cached list (or, in mock
 * mode, the seed store). This is the same trade the runs domain took for
 * `getRun(runId)` (#S9 backlog notes a future detail endpoint).
 *
 * Callers that need a fresh read should use `useInboxTicketQuery` so the
 * cached list is at least TanStack-managed; ad-hoc callers can hit
 * `findSeedInboxTicket(ticketId)` for a synchronous read in mock mode.
 *
 * In real mode the screen's `ticketId` is the host's UUID — we filter the
 * list by it directly. Mock mode accepts either the seed id (`nt_4120`) or
 * the synthesised UUID: the screen may hold either depending on which page
 * the operator came through.
 */
async function getInboxTicket(ticketId: string): Promise<Ticket | null> {
  if (env.useMock) {
    const seed = findSeedInboxTicket(ticketId)
    if (seed) {
      return mapSeedTicketToTicket(seed)
    }
    // Fall back to the synthesised UUID: the mapper writes the seed id into
    // `externalId`, so we can recover it from any row in the mock list.
    const candidates = listSeedInboxTickets(undefined).map(mapSeedTicketToTicket)
    return candidates.find((entry) => entry.id === ticketId) ?? null
  }
  // Real mode: ask the host's list endpoint and pick the row out. The
  // `projectId` filter is unset on purpose — we do not know which project
  // the ticket belongs to from the id alone, and the host's list is the
  // whole platform's pending queue. The list-page approach is intentionally
  // broad: it survives partial unique-key mismatches and the absence of a
  // dedicated detail endpoint. A detail endpoint would replace this body.
  const page = await getApiV1Inbox({})
  const tickets = mapInboxToTickets(page)
  return tickets.find((entry) => entry.id === ticketId) ?? null
}

export function useInboxQuery(filters: InboxFilters) {
  return useQuery({
    queryKey: [...inboxQueryKey, filters] as const,
    queryFn: () => listInbox(filters),
  })
}

export function useInboxCatalogQuery(connectionId: string, page: number = 1) {
  return useQuery({
    queryKey: [...inboxCatalogQueryKey(connectionId), { page }] as const,
    queryFn: () => getInboxCatalog(connectionId, page),
    enabled: connectionId.length > 0,
  })
}

export function useInboxTicketQuery(ticketId: string) {
  return useQuery({
    queryKey: [...inboxQueryKey, "ticket", ticketId] as const,
    queryFn: () => getInboxTicket(ticketId),
    enabled: ticketId.length > 0,
  })
}
