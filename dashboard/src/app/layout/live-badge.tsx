import { useEffect, useState } from "react"

import { RUNS_POLL_INTERVAL_MS } from "@/shared/api/polling"
import { StatusBadge, Tooltip } from "@/shared/ui"
import type { RunsHubStatus } from "@/shared/realtime/runs-hub"

/**
 * The bar's one reading about *how true the numbers on screen are*. Two
 * pills, and the split between them is the split between "these are not the
 * real numbers" and "these are the real numbers, late":
 *
 * ## `Demo` — the data is not real
 *
 * - the build was launched in mock mode (`useMock === true`) — operator
 *   chose synthetic seeds, the hub never tried to connect;
 * - the hub reports `demo` while in real mode — the backend is unreachable.
 *
 * Both render the same "Demo" word in the same `waiting` hue: one shape
 * the operator learns once. The tooltip carries the actionable detail —
 * the exact command to leave mock mode, or that the hub is offline —
 * because the badge's surface is too small to carry it.
 *
 * ## `Polling` — the data is real and behind
 *
 * `polling` is real mode with no live socket: the socket never opened, or
 * it opened and died, and the refresh layer is a timer. Nothing is wrong
 * with the numbers, but they are up to one poll cadence old and they no
 * longer move on their own — a run that finished four seconds ago still
 * reads `running` here. That is worth one word, because the alternative is
 * an operator watching a stalled board and believing it.
 *
 * It takes the `queued` hue rather than `waiting` on purpose: amber is
 * spoken for by "the data is not real", and this pill says something else.
 *
 * ### Why it waits a cadence first
 *
 * `polling` is also the *starting* status — `runs-hub.ts` opens on it and
 * stays there until the handshake lands — so a pill that appeared the
 * instant the status read `polling` would flash on every cold load and
 * every reconnect, and a badge that blinks during normal operation is a
 * badge nobody reads when it finally means something.
 *
 * So the pill waits one poll cycle. `RUNS_POLL_INTERVAL_MS` is imported
 * rather than restated because the two numbers are the same number: the
 * grace window is "long enough that the polling layer has had its turn",
 * and a hand-copied constant here would drift the day the cadence moves.
 * A socket that comes up inside that window is never mentioned at all.
 *
 * The clock resets in the effect's teardown rather than in its body —
 * `react-hooks/set-state-in-effect` is an error outside `shared/`, and a
 * teardown reset is the honest shape anyway: leaving `polling` is exactly
 * the moment the count stops meaning anything.
 *
 * ## Real + live renders nothing
 *
 * The absence of a pill is what the bar says for "you're pointed at the
 * real thing, and it is arriving live": a positive "live" pill would make
 * this chrome louder than the one reading it tells.
 *
 * The visual comes from `StatusBadge`'s `data-status` — `[data-status]` in
 * `tokens.css` is what hands the badge its `--hue`, and `StatusBadge` owns
 * the wash, border, mono font and small size. No per-badge CSS module.
 */
export interface LiveBadgeProps {
  useMock: boolean
  status: RunsHubStatus
}

export function LiveBadge({ useMock, status }: LiveBadgeProps) {
  // Only real mode can be "late" — mock mode has no socket to lose, and its
  // own pill already outranks this one.
  const lagging = useSettledPolling(!useMock && status === "polling")

  // Env mock OR a degraded hub: the data is synthetic either way.
  if (useMock || status === "demo") {
    return (
      <Tooltip content={explanation(useMock, status)}>
        <span data-test="demo-badge">
          <StatusBadge status="waiting" size="sm">
            Demo
          </StatusBadge>
        </span>
      </Tooltip>
    )
  }

  if (lagging) {
    return (
      <Tooltip content={pollingExplanation()}>
        <span data-test="polling-badge">
          <StatusBadge status="queued" size="sm">
            Polling
          </StatusBadge>
        </span>
      </Tooltip>
    )
  }

  return null
}

/**
 * `true` once the hub has been polling for a whole poll cycle without a
 * socket — the grace window that keeps the pill off cold loads and
 * reconnects. `false` again the moment the hub leaves that state.
 */
function useSettledPolling(watching: boolean): boolean {
  const [settled, setSettled] = useState(false)

  useEffect(() => {
    if (!watching) {
      return
    }

    const timer = setTimeout(() => {
      setSettled(true)
    }, RUNS_POLL_INTERVAL_MS)

    return () => {
      clearTimeout(timer)
      // Teardown, not the body: this runs when the hub leaves `polling`
      // (or the bar unmounts), which is precisely when a settled count
      // stops being true. Setting it in the body would both be wrong and
      // trip `react-hooks/set-state-in-effect`.
      setSettled(false)
    }
  }, [watching])

  // `watching &&` rather than `settled` alone: the teardown that resets the
  // count runs *after* the render that stopped watching, so returning the raw
  // flag would paint the pill for one frame on the way back to live.
  return watching && settled
}

function explanation(useMock: boolean, status: RunsHubStatus): string {
  // Mock + hub degraded is the operator's worst day — name both so the
  // tooltip does not send them chasing the wrong fix.
  if (useMock && status === "demo") {
    return "Working with synthetic data, backend unreachable"
  }
  // Mock + a "live" hub is unusual (mock mode never opens a socket, but
  // tests pin it): the actionable sentence is the same as mock alone.
  if (useMock) {
    return "Working with synthetic data — bun run dev:real to switch"
  }
  // Real + hub degraded: the backend is the only thing missing.
  return "Backend unreachable — showing last known state"
}

function pollingExplanation(): string {
  // The cadence is read off the same constant the grace window uses, so the
  // sentence can never promise a freshness the polling layer is not keeping.
  const seconds = Math.round(RUNS_POLL_INTERVAL_MS / 1000)
  return `Live updates are down — this screen refreshes every ${seconds}s instead`
}
