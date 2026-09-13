import { Tooltip } from "@/shared/ui"
import { env } from "@/shared/config/env"

import styles from "./mock-mode-badge.module.css"

/**
 * The "this is synthetic data" pill — the explicit answer to "is the host
 * actually running behind this dashboard?".
 *
 * Sits next to <LiveBadge /> in the topbar because that is where the
 * "how is this screen being served" reading already lives; this is a second
 * answer for a different question — *mode* (seeds vs live API), not
 * *freshness* (hub up, polling, or static). The two can drift in real
 * deployments: a working hub says "live" while the underlying mode is
 * still mock if a screen falls back to the seed, so the question is worth
 * answering on its own.
 *
 * Hidden in real mode: a "mock" pill above a real backend would be a lie,
 * and the topbar's other readings ("live" / "polling") already cover what
 * is happening then. The mirror of the vite startup banner in
 * `vite.config.ts` — operator sees it once on boot, every screen reasserts
 * it while open.
 *
 * Renders as a `<button>` rather than a `<span>` because the wrapping
 * <Tooltip /> needs an interactive ARIA role for React Aria's focus
 * machinery, and the pill's accessible name ("mock") is the only label
 * assistive tech needs — there is no onClick handler, the cursor stays
 * default, and the chrome is what an operator reads, not what they
 * activate.
 */
export function MockModeBadge() {
  if (!env.useMock) {
    return null
  }

  return (
    <Tooltip content="Working with synthetic data — bun run dev:real to switch">
      <button
        type="button"
        data-test="mock-mode-badge"
        data-mock-mode="true"
        className={styles.badge}
      >
        mock
      </button>
    </Tooltip>
  )
}