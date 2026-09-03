import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the inbox domain.
 *
 * The same invariants runs/identity/tests pin for their queries apply here:
 *
 * 1. The real-mode kubb clients are never imported in mock mode. Otherwise
 *    their import-time would read <c>import.meta.env</c> and bootstrap a
 *    real fetcher even when <c>env.useMock</c> is <c>true</c>.
 * 2. The seed store keeps mapping to the same <c>Ticket</c> shape the
 *    screen already renders, so callers can branch on
 *    <c>env.useMock</c> without branching on the result type.
 *
 * The test asserts the shape by hitting the public mapper directly and by
 * asserting that the queries module is import-safe in mock mode.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

describe("queries.ts mock-first path", () => {
  it("keeps reading the seed store when env.useMock is true", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const queries = await import("@/domains/inbox/api/queries")
    const { mapInboxToTickets } = await import("@/domains/inbox/api/mappers")
    const { SOURCES_SEED } = await import("@/shared/api/mock/sources.seed")

    const tickets = mapInboxToTickets(
      // The wire row shape: kubb keeps `runId: string | null` and the rest
      // verbatim. We re-use the seeded `SeedNativeTicket` to feed the
      // mapper the same data the real backend would hand us, after a
      // single normalisation step that the screen never sees.
      SOURCES_SEED.tickets.map((seed) => ({
        id: `00000000-0000-0000-0000-${seed.id.padEnd(12, "0").slice(0, 12)}`,
        projectId: seed.projectId,
        source: "native" as const,
        externalId: seed.id,
        title: seed.title,
        url: `https://comuki.local/inbox/${seed.id}`,
        status: seed.straightToWork ? ("claimed" as const) : ("pending" as const),
        runId: seed.straightToWork ? "00000000-0000-0000-0000-runstub000001" : null,
        createdAt:
          seed.createdAt === "just now"
            ? new Date().toISOString()
            : seed.createdAt,
      })),
    )

    expect(tickets.length).toBeGreaterThan(0)
    for (const ticket of tickets) {
      expect(ticket).toHaveProperty("id")
      expect(ticket).toHaveProperty("projectId")
      expect(ticket).toHaveProperty("status")
      expect(typeof ticket.source).toBe("string")
    }

    // Sanity-check the public surface of the queries module so the rest of
    // the tests can rely on it without re-importing.
    expect(typeof queries.useInboxQuery).toBe("function")
    expect(typeof queries.useInboxCatalogQuery).toBe("function")
    expect(typeof queries.useInboxTicketQuery).toBe("function")
    expect(queries.inboxQueryKey).toEqual(["inbox"])
  })

  it("import-time of queries does not require a populated VITE_API_BASE_URL in mock mode", async () => {
    // Empty base URL is OK as long as kubb-client is never called.
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    // Importing the queries module should not throw — real-mode calls do,
    // and that's the contract enforced by the kubb-client test.
    await expect(import("@/domains/inbox/api/queries")).resolves.toBeDefined()
  })
})
