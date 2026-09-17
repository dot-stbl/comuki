/**
 * Imperative glue of the ops pack: the one fetch that materialises the
 * pinned runs panel. Pure formatting lives in `lib/runsfeed.ts`;
 * `commands/chat.tsx` patches the result into the active session and
 * re-fetches it on the `RUNS_FEED_REFRESH_MS` cadence.
 *
 * Never throws — transport failures come back as a panel with
 * `refreshError` set (and no rows), which the caller merges so a blip
 * does not wipe a healthy panel.
 *
 * The cycle through `./chat` (describeError) is benign: both sides
 * only call each other inside function bodies, never at module init.
 */
import { ComukiClient } from "../lib/client"
import {
  RUNS_FEED_PAGE_SIZE,
  runsFeedRows,
  type RunsFeedPanel,
} from "../lib/runsfeed"
import { describeError } from "./chat"

/**
 * Fetches the newest runs page (project names resolved best-effort —
 * an unreachable projects endpoint degrades the column to short ids,
 * not to a failed panel).
 */
export async function fetchRunsFeedPanel(
  client: ComukiClient
): Promise<RunsFeedPanel> {
  try {
    const [page, projects] = await Promise.all([
      client.runs(1, RUNS_FEED_PAGE_SIZE),
      client.projects().catch(() => []),
    ])
    const names = new Map(projects.map((project) => [project.id, project.slug]))
    return {
      rows: runsFeedRows(page, names),
      total: page.total,
      fetchedAt: Date.now(),
      refreshError: null,
    }
  } catch (error) {
    return {
      rows: [],
      total: 0,
      fetchedAt: Date.now(),
      refreshError: describeError(error),
    }
  }
}
