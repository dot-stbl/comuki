import { env } from "@/shared/config/env"

/**
 * The polling fallback — the refresh layer that stays alive when the
 * SignalR connection is not (mock mode, a dead socket, a proxy that ate the
 * upgrade). Each domain names its own cadence here so the numbers are in one
 * place and the "how stale can this screen be" question has one answer per
 * surface, not per file.
 *
 * Error semantics: a refetch interval keeps firing on an errored query by
 * default, which would turn a hard-down backend into a request every
 * cadence. `pollEvery` backs off four-fold while the query holds an error —
 * the retry policy (`retry: 1` in `query-client.ts`) keeps its word, and the
 * interval never hammers.
 */

/** The duty list — a live run changes its column within fifteen seconds. */
export const RUNS_POLL_INTERVAL_MS = 15_000

/** The queue board — same freshness class as the runs list. */
export const QUEUE_POLL_INTERVAL_MS = 15_000

/** The intake lists (inbox + tasks) — a claimed ticket leaves within a minute. */
export const INBOX_POLL_INTERVAL_MS = 30_000

/** An open conversation's transcript — the console is a live surface. */
export const CHAT_MESSAGES_POLL_INTERVAL_MS = 10_000

/** How much slower to poll while the query holds an error. */
const ERROR_BACKOFF_FACTOR = 4

/** Enough of a query for a poll decision: the presence of an error. */
interface PollingQuery {
  readonly state: { readonly error: unknown }
}

/**
 * The interval function: the cadence while healthy, four times slower while
 * the query holds an error. Never `false` — a transient failure must not
 * silently switch a screen to no-refresh; it slows down instead.
 */
export function pollEvery(
  intervalMs: number,
): (query: PollingQuery) => number {
  return (query) =>
    query.state.error ? intervalMs * ERROR_BACKOFF_FACTOR : intervalMs
}

/**
 * The refetchInterval option for a live-data query: the cadence in real
 * mode, **off** in mock mode. Mock data is seed data — polling it would
 * churn storybook and tests for nothing, so `false` keeps the mock-first
 * flow exactly as it was.
 */
export function livePolling(
  intervalMs: number,
): false | ((query: PollingQuery) => number) {
  return env.useMock ? false : pollEvery(intervalMs)
}
