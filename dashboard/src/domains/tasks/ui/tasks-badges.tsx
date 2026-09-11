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
import {
  isNativeIntake,
  providerBrand,
  providerLabel,
} from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import type { TaskPriority, TaskStatus } from "@/domains/tasks/model/types"
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
 * same registry the intake cards and the sources table read, so one provider
 * is one glyph everywhere it appears. Yandex Tracker is the spelled exception
 * (no monochrome mark exists; see `Provider.brand`), and takes a board glyph
 * rather than a shape nobody could name — as does any provider this build has
 * not learned. Every mark is decorative here: the badge's own text — the
 * tracker id, or the intake's own word — is the reading.
 */
function SourceMark({ source }: { source: ProviderKey }) {
  const brand = providerBrand(source)
  if (!brand) {
    return <SquareKanban className={styles.icon} aria-hidden="true" />
  }
  return (
    <BrandIcon brand={brand} size="xs" label={null} className={styles.icon} />
  )
}

export interface TaskSourceBadgeProps {
  source: ProviderKey
  /** The tracker's own id. For a ticket off a branch this badge *is* the id. */
  id: string
  className?: string
}

export function TaskSourceBadge({
  source,
  id,
  className,
}: TaskSourceBadgeProps) {
  /* The product's own intake has no tracker id to show, so the badge says the
     provider instead — which is the honest limit of a column that is a badge
     rather than a value. Every other provider, learned or not, has an id on
     the other side of a wire. */
  const native = isNativeIntake(source)
  return (
    <span
      data-test="task-source-badge"
      data-source={source}
      /* One styling class for every tracker stamp and one for the product's
         own intake: within the two groups the mark and the id carry which
         provider it is, and a hue per vendor would be confetti on the
         Colourless Chrome Rule's one surface it never allowed. */
      className={cn(
        styles.badge,
        native ? styles.native : styles.tracker,
        className
      )}
    >
      <SourceMark source={source} />
      {native ? providerLabel(source) : id}
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
