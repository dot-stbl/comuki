import { describe, expect, it } from "vitest"

import {
  mapClaimTicketInputToClaimRequest,
  mapInboxCatalogToConnections,
  mapInboxToTickets,
  mapIntakeTicketViewToTicket,
  mapNativeTicketInputToCreateRequest,
  mapSeedTicketToTicket,
  normalizeTicketStatus,
} from "@/domains/inbox/api/mappers"
import type { IntakeTicketView } from "@/shared/api/_generated/types/IntakeTicketView"
import { SOURCES_SEED } from "@/shared/api/mock/sources.seed"

/**
 * Wire ↔ domain mappers for the kubb-generated inbox surface.
 *
 * The host returns a sparse row (`IntakeTicketView`) on every inbox-shaped
 * endpoint — pending list, catalog browse and the claim response. The
 * mapper turns one wire row into one domain `Ticket`; the rest compose.
 *
 * These assertions pin:
 *
 *  - the wire's `status: string` is normalised to the four-value union,
 *    with unknown values falling through to `"pending"` (a partial
 *    backend rollout should degrade the row, not the screen);
 *  - the wire's loose typing for `runId` (kubb keeps it `string | null`)
 *    carries through verbatim, including `null`;
 *  - the domain input types map to kubb's request shapes verbatim, with
 *    optional fields staying optional.
 *
 * The mock side (`mapSeedTicketToTicket`) is also pinned: synthesised
 * UUID + deep-link URL stay stable across reloads so a session's
 * optimistic writes do not flap.
 */

function ticketViewFixture(overrides: Partial<IntakeTicketView> = {}): IntakeTicketView {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    projectId: "00000000-0000-0000-0000-0000000000aa",
    source: "github",
    externalId: "42",
    title: "search-idx drops the last shard on a cold start",
    url: "https://github.com/comuki/web-app/issues/42",
    status: "pending",
    runId: null,
    createdAt: "2026-09-04T10:00:00.000+00:00",
    ...overrides,
  }
}

describe("normalizeTicketStatus", () => {
  it("accepts every value of the wire's closed status set", () => {
    expect(normalizeTicketStatus("pending")).toBe("pending")
    expect(normalizeTicketStatus("claimed")).toBe("claimed")
    expect(normalizeTicketStatus("done")).toBe("done")
    expect(normalizeTicketStatus("dismissed")).toBe("dismissed")
  })

  it("falls back to \"pending\" for unknown values", () => {
    expect(normalizeTicketStatus("admitted")).toBe("pending")
    expect(normalizeTicketStatus("")).toBe("pending")
    expect(normalizeTicketStatus("Pending")).toBe("pending")
  })
})

describe("mapIntakeTicketViewToTicket", () => {
  it("carries the host's row fields through verbatim", () => {
    const ticket = mapIntakeTicketViewToTicket(ticketViewFixture())

    expect(ticket.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(ticket.projectId).toBe("00000000-0000-0000-0000-0000000000aa")
    expect(ticket.source).toBe("github")
    expect(ticket.externalId).toBe("42")
    expect(ticket.title).toBe("search-idx drops the last shard on a cold start")
    expect(ticket.url).toBe("https://github.com/comuki/web-app/issues/42")
    expect(ticket.status).toBe("pending")
    expect(ticket.runId).toBeNull()
    expect(ticket.createdAt).toBe("2026-09-04T10:00:00.000+00:00")
  })

  it("maps a claimed row's runId through unchanged", () => {
    const ticket = mapIntakeTicketViewToTicket(
      ticketViewFixture({
        status: "claimed",
        runId: "00000000-0000-0000-0000-00000000beef",
      }),
    )

    expect(ticket.status).toBe("claimed")
    expect(ticket.runId).toBe("00000000-0000-0000-0000-00000000beef")
  })

  it("defaults the kind to \"issue\" until the wire carries a discriminator", () => {
    const ticket = mapIntakeTicketViewToTicket(ticketViewFixture())

    expect(ticket.kind).toBe("issue")
  })

  it("normalises an unknown status to \"pending\"", () => {
    const ticket = mapIntakeTicketViewToTicket(
      ticketViewFixture({ status: "queued-for-claim" }),
    )

    expect(ticket.status).toBe("pending")
  })
})

describe("mapInboxToTickets", () => {
  it("maps every row of a wire list to a domain ticket", () => {
    const list: IntakeTicketView[] = [
      ticketViewFixture({ id: "00000000-0000-0000-0000-000000000001", status: "pending" }),
      ticketViewFixture({
        id: "00000000-0000-0000-0000-000000000002",
        source: "gitlab",
        externalId: "9",
        status: "claimed",
        runId: "00000000-0000-0000-0000-00000000beef",
      }),
      ticketViewFixture({
        id: "00000000-0000-0000-0000-000000000003",
        status: "dismissed",
      }),
    ]

    const tickets = mapInboxToTickets(list)

    expect(tickets).toHaveLength(3)
    expect(tickets[0]?.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(tickets[0]?.status).toBe("pending")
    expect(tickets[1]?.source).toBe("gitlab")
    expect(tickets[1]?.status).toBe("claimed")
    expect(tickets[1]?.runId).toBe("00000000-0000-0000-0000-00000000beef")
    expect(tickets[2]?.status).toBe("dismissed")
  })

  it("returns an empty array when the host's list is empty", () => {
    expect(mapInboxToTickets([])).toEqual([])
  })
})

describe("mapInboxCatalogToConnections", () => {
  it("carries the connection id on the projection, not on each row", () => {
    const projection = mapInboxCatalogToConnections(
      [
        ticketViewFixture({ id: "00000000-0000-0000-0000-000000000001" }),
        ticketViewFixture({ id: "00000000-0000-0000-0000-000000000002" }),
      ],
      "00000000-0000-0000-0000-src00000001",
    )

    expect(projection.connectionId).toBe("00000000-0000-0000-0000-src00000001")
    expect(projection.items).toHaveLength(2)
    expect(projection.items[0]?.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(projection.items[1]?.id).toBe("00000000-0000-0000-0000-000000000002")
  })

  it("returns an empty projection when the catalog page is empty", () => {
    const projection = mapInboxCatalogToConnections(
      [],
      "00000000-0000-0000-0000-src00000001",
    )

    expect(projection.connectionId).toBe("00000000-0000-0000-0000-src00000001")
    expect(projection.items).toEqual([])
  })
})

describe("mapClaimTicketInputToClaimRequest", () => {
  it("writes the ticketId field verbatim", () => {
    const body = mapClaimTicketInputToClaimRequest({ ticketId: "00000000-0000-0000-0000-00000000beef" })

    expect(body).toEqual({ ticketId: "00000000-0000-0000-0000-00000000beef" })
  })
})

describe("mapNativeTicketInputToCreateRequest", () => {
  it("passes the required fields through verbatim", () => {
    const body = mapNativeTicketInputToCreateRequest({
      projectId: "00000000-0000-0000-0000-0000000000aa",
      title: "ledger-core: money columns print with a float tail",
    })

    expect(body).toEqual({
      projectId: "00000000-0000-0000-0000-0000000000aa",
      title: "ledger-core: money columns print with a float tail",
      body: undefined,
      externalId: undefined,
      author: undefined,
    })
  })

  it("preserves optional fields when the caller provides them", () => {
    const body = mapNativeTicketInputToCreateRequest({
      projectId: "00000000-0000-0000-0000-0000000000aa",
      title: "...",
      body: "long form",
      externalId: "ext-1",
      author: "brad",
    })

    expect(body).toEqual({
      projectId: "00000000-0000-0000-0000-0000000000aa",
      title: "...",
      body: "long form",
      externalId: "ext-1",
      author: "brad",
    })
  })
})

describe("mapSeedTicketToTicket (mock side)", () => {
  it("synthesises a UUID id from the seed id", () => {
    const seed = SOURCES_SEED.tickets[0]
    expect(seed).toBeDefined()
    const ticket = mapSeedTicketToTicket(seed!)

    // Deterministic — same seed id always maps to the same UUID so a
    // session-stable mock survives a refetch.
    expect(ticket.id).toBe(`00000000-0000-0000-0000-${seed!.id.padEnd(12, "0").slice(0, 12)}`)
  })

  it("maps straightToWork=false to status=\"pending\" with no run id", () => {
    const seed = SOURCES_SEED.tickets.find((ticket) => !ticket.straightToWork)
    expect(seed).toBeDefined()

    const ticket = mapSeedTicketToTicket(seed!)

    expect(ticket.status).toBe("pending")
    expect(ticket.runId).toBeNull()
    expect(ticket.source).toBe("native")
  })

  it("maps straightToWork=true to status=\"claimed\" with a synthesised run id", () => {
    const seed = SOURCES_SEED.tickets.find((ticket) => ticket.straightToWork)
    expect(seed).toBeDefined()

    const ticket = mapSeedTicketToTicket(seed!)

    expect(ticket.status).toBe("claimed")
    expect(ticket.runId).toBe("00000000-0000-0000-0000-runstub000001")
  })

  it("rewrites \"just now\" createdAt to an ISO string the screen can render", () => {
    const before = new Date()
    const ticket = mapSeedTicketToTicket({
      ...SOURCES_SEED.tickets[0]!,
      createdAt: "just now",
    })
    const after = new Date()

    expect(new Date(ticket.createdAt).getTime()).toBeGreaterThanOrEqual(
      before.getTime(),
    )
    expect(new Date(ticket.createdAt).getTime()).toBeLessThanOrEqual(
      after.getTime(),
    )
  })

  it("passes an ISO seed createdAt through unchanged", () => {
    const ticket = mapSeedTicketToTicket({
      ...SOURCES_SEED.tickets[0]!,
      createdAt: "2026-08-28",
    })

    expect(ticket.createdAt).toBe("2026-08-28")
  })
})
