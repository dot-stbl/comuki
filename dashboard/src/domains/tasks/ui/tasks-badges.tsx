import type { ComponentType } from "react"
import {
  Activity,
  ChevronDown,
  ChevronsUp,
  Circle,
  Minus,
  Sparkles,
  SquareKanban,
} from "lucide-react"

import { cn } from "@/shared/lib/utils"
import { TASK_SOURCE_BRAND } from "@/domains/tasks/model/task-sources"
import type {
  TaskPriority,
  TaskSource,
  TaskStatus,
} from "@/domains/tasks/model/types"
import { BrandIcon } from "@/shared/ui"

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

/**
 * Where a ticket came from, drawn as the provider's own drained mark — the
 * same marks the intake cards and the sources table wear, so one provider is
 * one glyph everywhere it appears. Yandex Tracker is the spelled exception
 * (no monochrome mark exists; see `task-sources.ts`), and takes a board
 * glyph rather than a shape nobody could name. Every mark is decorative
 * here: the badge's own text — the tracker id, or the word "manual" — is
 * the reading.
 */
function SourceMark({ source }: { source: TaskSource }) {
  const brand = TASK_SOURCE_BRAND[source]
  if (!brand) {
    return <SquareKanban className={styles.icon} aria-hidden="true" />
  }
  return (
    <BrandIcon brand={brand} size="xs" label={null} className={styles.icon} />
  )
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
  return (
    <span
      data-test="task-source-badge"
      data-source={source}
      /* One styling class for the four tracker stamps and one for manual:
         within the two groups the mark and the id carry which provider it
         is, and a hue per vendor would be confetti on the Colourless Chrome
         Rule's one surface it never allowed. */
      className={cn(
        styles.badge,
        source === "manual" ? styles.manual : styles.tracker,
        className
      )}
    >
      <SourceMark source={source} />
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
