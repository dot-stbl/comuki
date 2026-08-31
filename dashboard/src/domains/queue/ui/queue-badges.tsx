import type { ComponentType } from "react"
import { Activity, Ban, Check, Hourglass, Lock, LogOut, Pause, X } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import type { WorkItemStatus, WorkerState } from "@/domains/queue/model/types"

import styles from "./queue-badges.module.css"

/**
 * Two badges, two vocabularies — and neither of them is the run's.
 *
 * `StatusBadge` in the kit speaks the six *run* statuses, and it would be the
 * wrong component here twice over: a work item is `succeeded`, not `success`,
 * and it can be `blocked` or `cancelled`, which no run status covers; a worker
 * is not in a status at all, it is in a state. Borrowing the kit badge would
 * have meant either lying about the word or widening a shared primitive to
 * carry a vocabulary only this screen speaks.
 *
 * They follow the kit's construction exactly, though, because that is what
 * makes them read as the same system: an icon, a hue and a hairline, sized
 * from the same tokens. Status is never hue alone — every value below has its
 * own silhouette, so the reading survives greyscale, and the two that carry
 * nothing urgent (`blocked`, `cancelled`) are deliberately given no hue at all.
 */

const statusIcons: Record<WorkItemStatus, ComponentType<{ className?: string }>> =
  {
    blocked: Lock,
    queued: Hourglass,
    running: Activity,
    succeeded: Check,
    failed: X,
    cancelled: Ban,
  }

export interface WorkStatusBadgeProps {
  status: WorkItemStatus
  className?: string
}

export function WorkStatusBadge({ status, className }: WorkStatusBadgeProps) {
  const Icon = statusIcons[status]

  return (
    <span
      data-test="work-status-badge"
      data-status={status}
      className={cn(styles.badge, styles[status], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {status}
    </span>
  )
}

const stateIcons: Record<WorkerState, ComponentType<{ className?: string }>> = {
  idle: Pause,
  busy: Activity,
  draining: LogOut,
}

export interface WorkerStateBadgeProps {
  state: WorkerState
  className?: string
}

export function WorkerStateBadge({ state, className }: WorkerStateBadgeProps) {
  const Icon = stateIcons[state]

  return (
    <span
      data-test="worker-state-badge"
      data-state={state}
      className={cn(styles.badge, styles[state], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {state}
    </span>
  )
}
