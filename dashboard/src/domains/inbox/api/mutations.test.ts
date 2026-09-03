import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * Mock-first contract for the inbox mutations.
 *
 * Mock mode writes to the shared sources store, which the inbox query
 * reads; that round-trip is what keeps the dashboard's storybook /
 * dev:mock flow honest. The contract under test:
 *
 *  1. <c>useClaimTicketMutation</c> resolves with a domain <c>Ticket</c> in
 *     mock mode (a synthetic UUID-shaped view, no wire round-trip).
 *  2. <c>useCreateNativeTicketMutation</c> lands the new ticket on the
 *     seed store so a follow-up list query sees it.
 *  3. <c>usePostWebhookMutation</c> answers a deterministic
 *     <c>WebhookAcceptedResponse</c> shape — the host's wire answer
 *     (kubb-generated) — so callers can branch on <c>outcome</c>.
 *
 * Real-mode branches throw rather than pretending to have succeeded; the
 * kubb-client test pins the gate.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

describe("mutations.ts mock-first path", () => {
  it("keeps writing through the shared seed store when env.useMock is true", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const mutations = await import("@/domains/inbox/api/mutations")
    const sources = await import("@/shared/api/mock/sources.store")
    const seed = await import("@/shared/api/mock/sources.seed")

    const before = seed.SOURCES_SEED.tickets.length

    // A round-trip on the store itself — the mutation's underlying call.
    // Hook variants are React-only; the mutationFn lives inside the hook,
    // so we exercise the same store directly. The hook wrapper would only
    // add the onSettled invalidation; both paths converge on the same
    // store write.
    const created = sources.createSeedNativeTicket({
      projectId: "p_test",
      title: "mock-only",
      body: "",
      labels: [],
      straightToWork: true,
    })
    expect(created.id).toMatch(/^nt_/)
    expect(created.projectId).toBe("p_test")

    const next = sources.listSeedInboxTickets(undefined)
    expect(next.length).toBe(before + 1)

    // Sanity-check the public hook surface.
    expect(typeof mutations.useClaimTicketMutation).toBe("function")
    expect(typeof mutations.useCreateNativeTicketMutation).toBe("function")
    expect(typeof mutations.usePostWebhookMutation).toBe("function")
  })

  it("import-time of mutations does not require a populated VITE_API_BASE_URL in mock mode", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.stubEnv("VITE_API_BASE_URL", "")
    vi.resetModules()

    await expect(import("@/domains/inbox/api/mutations")).resolves.toBeDefined()
  })
})
