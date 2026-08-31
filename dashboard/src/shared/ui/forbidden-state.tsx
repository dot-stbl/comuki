import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./forbidden-state.module.css"

export interface ForbiddenStateProps {
  /**
   * The sentence naming the roles that would open it — `needsLabel(permission)`
   * from `shared/session`.
   *
   * Passed in rather than derived here on purpose: the kit renders, the app
   * decides policy. A component that imported the permission matrix would drag
   * the product's access rules into every Storybook story that shows a state.
   */
  needs: string
  /** What is closed, in the product's own words — a screen or a list. */
  subject?: string
  /** An extra line, when a screen has something more useful to add. */
  children?: ReactNode
  className?: string
}

/**
 * The fourth list state.
 *
 * §17 names four — Empty, Loading, Error, Forbidden — and this is the one that
 * was missing. It sits in the same slot the other three use and carries the
 * same weight: a closed view is not a bigger event than an empty one, so it
 * gets one title and one measured line, not a warning panel.
 *
 * It says what is missing and stops. No lock glyph, no red, no "you are not
 * allowed" — the operator did nothing wrong, they simply hold a different role,
 * and the useful half of the message is which role would work.
 */
export function ForbiddenState({
  needs,
  subject = "This view",
  children,
  className,
}: ForbiddenStateProps) {
  return (
    <div className={cn(styles.state, className)} data-test="forbidden-state">
      <p className={styles.title}>{subject} is closed to your roles</p>
      <p className={styles.body}>
        {needs} — ask for the role, or switch to a project where you already
        hold it.
      </p>
      {children}
    </div>
  )
}
