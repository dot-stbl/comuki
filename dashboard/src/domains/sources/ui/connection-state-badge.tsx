import type { ComponentType } from "react"
import { AlertTriangle, Check, PowerOff } from "lucide-react"

import type { SourceState } from "@/domains/sources/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./connection-state-badge.module.css"

export interface ConnectionStateBadgeProps {
  state: SourceState
  className?: string
}

/**
 * A distinct mark per state, so hue is never the only channel: a tick, a
 * warning triangle and a cut power. The three are different shapes at a
 * glance and different words when read aloud.
 */
const marks: Record<SourceState, ComponentType<{ className?: string }>> = {
  connected: Check,
  error: AlertTriangle,
  disabled: PowerOff,
}

/**
 * How a connection stands, in the requirements' own three words.
 *
 * Not a `StatusBadge`. That component speaks the product's six *run* statuses,
 * and a connection is never queued or escalated — a badge that borrowed the
 * vocabulary would be describing the wrong kind of thing in the right colours.
 * This is the same shape, built from the same tokens, with its own three words.
 */
export function ConnectionStateBadge({
  state,
  className,
}: ConnectionStateBadgeProps) {
  const Mark = marks[state]

  return (
    <span
      className={cn(styles.badge, styles[state], className)}
      data-test="connection-state"
      data-state={state}
    >
      <Mark className={styles.icon} />
      {state}
    </span>
  )
}
