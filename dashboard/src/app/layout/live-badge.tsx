import { StatusBadge, Tooltip } from "@/shared/ui"
import type { RunsHubStatus } from "@/shared/realtime/runs-hub"

/**
 * The "this is not the real backend" pill — one reading that covers both
 * reasons a screen is not standing behind the host:
 *
 * - the build was launched in mock mode (`useMock === true`) — operator
 *   chose synthetic seeds, the hub never tried to connect;
 * - the hub degraded to `demo` status while in real mode — the backend is
 *   unreachable and the polling layer is the only refresh there is.
 *
 * Both render the same "Demo" word in the same `waiting` hue: one shape
 * the operator learns once. The tooltip carries the actionable detail —
 * the exact command to leave mock mode, or that the hub is offline —
 * because the badge's surface is too small to carry it.
 *
 * Real + live renders nothing. The absence of the pill is what the bar
 * says for "you're pointed at the real thing": a positive "real" pill
 * would make this chrome louder than the one reading it tells. Real +
 * `polling` also renders nothing — the polling layer is the platform's
 * expected degradation when no socket is up, and a pill that appears
 * and disappears as the socket reconnects would compete with the duty
 * screen for "what just changed".
 *
 * The visual comes from `StatusBadge`'s `data-status="waiting"` —
 * `[data-status="waiting"]` in `tokens.css` is what hands the badge its
 * `--hue`, and `StatusBadge` owns the wash, border, mono font and small
 * size. No per-badge CSS module: the merge drops the mock-mode-badge's
 * bespoke pill chrome (uppercase, tracking-label) because the new shape
 * sits in the same status-badge family as the rest of the bar's
 * readings.
 */
export interface LiveBadgeProps {
  useMock: boolean
  status: RunsHubStatus
}

export function LiveBadge({ useMock, status }: LiveBadgeProps) {
  // One boolean covers both reasons: env mock OR a degraded hub. Real +
  // live + polling all skip the pill — see the doc above for the
  // "polling also nothing" reasoning.
  if (!useMock && status !== "demo") {
    return null
  }

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
