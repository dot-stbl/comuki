import { Check, MinusCircle, X } from "lucide-react"

import type { VerifyResult } from "@/domains/verify/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./verify-result-badge.module.css"

export interface VerifyResultBadgeProps {
  /** `null` when no run has ever reached this command. */
  result: VerifyResult | null
  className?: string
}

/**
 * What this check said the last time anything ran it.
 *
 * Three readings, not two. "never ran" is its own state and looks like its own
 * state: a command committed to git that no run has reached is a hole in the
 * gate's coverage, and rolling it in with the failures — or, worse, drawing it
 * as a blank — would hide the one thing on this screen that nobody would
 * otherwise notice.
 */
export function VerifyResultBadge({ result, className }: VerifyResultBadgeProps) {
  if (!result) {
    return (
      <span
        className={cn(styles.badge, styles.never, className)}
        data-test="verify-result"
        data-outcome="never"
      >
        <MinusCircle className={styles.icon} />
        never ran
      </span>
    )
  }

  const failed = result.outcome === "failed"
  const Mark = failed ? X : Check

  return (
    <span
      className={cn(styles.badge, failed ? styles.failed : styles.passed, className)}
      data-test="verify-result"
      data-outcome={failed ? "failed" : "passed"}
    >
      <Mark className={styles.icon} />
      {failed ? "failed" : "passed"}
    </span>
  )
}
