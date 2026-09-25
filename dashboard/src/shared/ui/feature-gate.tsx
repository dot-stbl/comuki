import type { ReactNode } from "react"
import { Lock } from "lucide-react"

import styles from "./feature-gate.module.css"

/**
 * The three states the gate distinguishes. `available` is a separate
 * tri-state (`true` / `false` / `undefined`) rather than a boolean so a
 * caller can pass an in-flight snapshot's `undefined` directly — the
 * optimistic render keeps children showing until the snapshot lands.
 */
export interface FeatureGateProps {
  /**
   * The closed-vocabulary feature key the gate is consulting. Echoed
   * verbatim in the locked fallback so the user can see exactly which
   * capability they have not covered; the dashboard's gating keywords
   * (`multi-repo`, `enterprise-sso`, …) are themselves a closed set,
   * so there is no UI to "round" them.
   */
  readonly feature: string
  /**
   * `true` — render children.
   * `false` — render the locked fallback (or the caller's override).
   * `undefined` — the snapshot has not landed yet. Render children
   *   anyway: flashing a locked state at the first paint would tell
   *   the reader the page is broken, and the gate's enforcement lives
   *   on the server. The optimistic default is deliberate.
   */
  readonly available: boolean | undefined
  /**
   * What to render when `available === false`. Defaults to the kit's
   * own locked panel — a small locked affordance with the feature key
   * and one lowercase sentence.
   */
  readonly fallback?: ReactNode
  /** What the gate is guarding. Rendered when `available !== false`. */
  readonly children: ReactNode
}

/**
 * A declarative capability gate: renders `children` when the named
 * feature is covered, a tasteful locked fallback when it isn't.
 *
 * The gate is presentational — the caller resolves the snapshot and
 * passes the result. Keeping the data flow outside the kit is the same
 * reason every other UI primitive is presentational: tests for the
 * gate assert its rendering against the tri-state, not against the
 * snapshot it would have to mock otherwise.
 */
export function FeatureGate({
  feature,
  available,
  fallback,
  children,
}: FeatureGateProps) {
  if (available !== false) {
    return <>{children}</>
  }

  if (fallback !== undefined) {
    return <>{fallback}</>
  }

  return (
    <div
      className={styles.locked}
      data-test="feature-gate-locked"
      data-feature={feature}
      role="status"
    >
      <Lock aria-hidden="true" className={styles.icon} />
      <p className={styles.message}>
        <span className={styles.key}>{feature}</span> is not in this edition.
      </p>
    </div>
  )
}
