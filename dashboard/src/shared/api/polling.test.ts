import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The polling fallback's own contract:
 *
 * - a healthy query polls on its cadence; a query holding an error backs
 *   off four-fold instead of hammering a down backend;
 * - the cadence is **off** in mock mode (seed data does not go stale, and
 *   storybook/tests must not churn) and on in real mode — the split the
 *   five live-data queries (runs, queue, inbox, tasks, chat messages) all
 *   ride through `livePolling`.
 *
 * `env` is computed from `import.meta.env` at module load, so the mock/real
 * split is exercised by resetting modules between cases — the same pattern
 * `kubb-client.test.ts` uses.
 */

afterEach(() => {
  vi.resetModules()
  vi.unstubAllEnvs()
})

describe("pollEvery", () => {
  it("returns the cadence while healthy and backs off four-fold while the query holds an error", async () => {
    const { pollEvery } = await import("@/shared/api/polling")

    const interval = pollEvery(15_000)
    expect(interval({ state: { error: null } })).toBe(15_000)
    expect(interval({ state: { error: new Error("request failed 502") } })).toBe(
      60_000,
    )
  })

  it("never turns an error into no polling at all — slow, not silent", async () => {
    const { pollEvery } = await import("@/shared/api/polling")

    const interval = pollEvery(10_000)
    expect(typeof interval({ state: { error: new Error("down") } })).toBe(
      "number",
    )
  })
})

describe("livePolling", () => {
  it("is off in mock mode — seed data is not polled", async () => {
    vi.stubEnv("VITE_USE_MOCK", "true")
    vi.resetModules()

    const { livePolling, RUNS_POLL_INTERVAL_MS } = await import(
      "@/shared/api/polling"
    )

    expect(livePolling(RUNS_POLL_INTERVAL_MS)).toBe(false)
  })

  it("polls on cadence in real mode, with the error backoff preserved", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.resetModules()

    const {
      livePolling,
      RUNS_POLL_INTERVAL_MS,
      QUEUE_POLL_INTERVAL_MS,
      INBOX_POLL_INTERVAL_MS,
      CHAT_MESSAGES_POLL_INTERVAL_MS,
    } = await import("@/shared/api/polling")

    for (const cadence of [
      RUNS_POLL_INTERVAL_MS,
      QUEUE_POLL_INTERVAL_MS,
      INBOX_POLL_INTERVAL_MS,
      CHAT_MESSAGES_POLL_INTERVAL_MS,
    ]) {
      const interval = livePolling(cadence)
      expect(typeof interval).toBe("function")
      const onCadence = interval as (query: {
        state: { error: unknown }
      }) => number
      expect(onCadence({ state: { error: null } })).toBe(cadence)
      expect(
        onCadence({ state: { error: new Error("down") } }),
      ).toBe(cadence * 4)
    }
  })

  it("the cadences match the fallback design — 15s runs/queue, 30s intake, 10s chat", async () => {
    vi.stubEnv("VITE_USE_MOCK", "false")
    vi.resetModules()

    const polling = await import("@/shared/api/polling")

    expect(polling.RUNS_POLL_INTERVAL_MS).toBe(15_000)
    expect(polling.QUEUE_POLL_INTERVAL_MS).toBe(15_000)
    expect(polling.INBOX_POLL_INTERVAL_MS).toBe(30_000)
    expect(polling.CHAT_MESSAGES_POLL_INTERVAL_MS).toBe(10_000)
  })
})
