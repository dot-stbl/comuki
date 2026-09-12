import { StatusBadge, Tooltip } from "@/shared/ui"
import { useRunsHubStatus } from "@/shared/realtime/runs-hub"

/**
 * The connection indicator — one honest word about how this screen's data
 * arrives, in the badge the product already uses for state.
 *
 * Three readings, and no fourth:
 *
 * - `live` (success) — the runs-hub socket is up; events invalidate the
 *   queries they belong to as they happen.
 * - `polling` (queued) — real mode, no socket. The polling fallback refreshes
 *   the same queries on their cadences; the data is slower, not wrong.
 * - `demo` (waiting) — mock mode. There is no backend; the seed is the data,
 *   and nothing refreshes because nothing changes.
 *
 * Colours are the six status tokens via `StatusBadge`'s `data-status` — no
 * new colour exists for a connection, and none is needed: the badge borrows
 * the readings (green = arriving, neutral = periodic, amber = not the real
 * thing) the duty screen already taught.
 */
export function LiveBadge() {
  const status = useRunsHubStatus()

  return (
    <Tooltip content={explanation(status)}>
      <span data-test="live-badge">
        <StatusBadge
          status={
            status === "live" ? "success" : status === "polling" ? "queued" : "waiting"
          }
          size="sm"
        >
          {status}
        </StatusBadge>
      </span>
    </Tooltip>
  )
}

function explanation(status: "live" | "polling" | "demo"): string {
  if (status === "live") {
    return "Hub connected — updates arrive as they happen"
  }
  if (status === "polling") {
    return "No hub connection — screens refresh on their poll cadence"
  }
  return "Demo data — no backend is connected"
}
