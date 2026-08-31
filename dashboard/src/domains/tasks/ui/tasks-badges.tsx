import type { ComponentType } from "react"
import {
  Activity,
  ChevronDown,
  ChevronsUp,
  Circle,
  GitBranch,
  Minus,
  Plus,
  Sparkles,
} from "lucide-react"

import { cn } from "@/shared/lib/utils"
import type {
  TaskPriority,
  TaskSource,
  TaskStatus,
} from "@/domains/tasks/model/types"

import styles from "./tasks-badges.module.css"

/**
 * The backlog's three marks — and none of them is the run's.
 *
 * `StatusBadge` in the kit speaks the six *run* statuses, and a ticket is in
 * none of them: it is `new`, `queued` or `planning`, which is intake order and
 * not a run's life. Borrowing the kit badge would have meant either lying about
 * the word or widening a shared primitive to carry a vocabulary only this
 * screen speaks — so these follow the kit's construction exactly instead: an
 * icon, a hue and a hairline, sized from the same tokens, at the small step
 * because they sit in compact table rows.
 *
 * Every one of them carries a silhouette as well as a hue. The priority mark is
 * where that actually changed something: it used to be hue alone — a coral
 * wash for `high` and a grey one for `normal` — which said nothing in greyscale
 * and nothing at all to a red-green eye, in a column whose entire job is to be
 * scanned down the page.
 */

const sourceIcons: Record<TaskSource, ComponentType<{ className?: string }>> = {
  manual: Plus,
  jira: GitBranch,
}

export interface TaskSourceBadgeProps {
  source: TaskSource
  /** The tracker's own id. For a ticket off a branch this badge *is* the id. */
  id: string
  className?: string
}

export function TaskSourceBadge({
  source,
  id,
  className,
}: TaskSourceBadgeProps) {
  const Icon = sourceIcons[source]

  return (
    <span
      data-test="task-source-badge"
      data-source={source}
      className={cn(styles.badge, styles[source], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {source === "manual" ? "manual" : id}
    </span>
  )
}

const priorityIcons: Record<
  TaskPriority,
  ComponentType<{ className?: string }>
> = {
  high: ChevronsUp,
  normal: Minus,
  low: ChevronDown,
}

export interface TaskPriorityBadgeProps {
  priority: TaskPriority
  className?: string
}

export function TaskPriorityBadge({
  priority,
  className,
}: TaskPriorityBadgeProps) {
  const Icon = priorityIcons[priority]

  return (
    <span
      data-test="task-priority-badge"
      data-priority={priority}
      className={cn(styles.badge, styles[priority], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {priority}
    </span>
  )
}

const statusIcons: Record<TaskStatus, ComponentType<{ className?: string }>> = {
  new: Sparkles,
  queued: Circle,
  planning: Activity,
}

export interface TaskStatusBadgeProps {
  status: TaskStatus
  className?: string
}

export function TaskStatusBadge({ status, className }: TaskStatusBadgeProps) {
  const Icon = statusIcons[status]

  return (
    <span
      data-test="task-status-badge"
      data-status={status}
      className={cn(styles.badge, styles[status], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {status}
    </span>
  )
}
