import type { ComponentType } from "react"
import { ChevronDown, ChevronsUp, Minus } from "lucide-react"

import { cn } from "@/shared/lib/utils"
import type { TaskPriority } from "@/domains/tasks/model/types"

import styles from "./task-priority-field.module.css"

/* The same silhouette-per-value the backlog's badge runs on — see
   `tasks-badges.tsx`. Imported here rather than exported from there because
   the badge owns no vocabulary: the icon and the hue are the shared fact,
   and two components reading one table keeps them from drifting. */
const PRIORITY_ICONS: Record<
  TaskPriority,
  ComponentType<{ className?: string; "aria-hidden"?: boolean | "true" }>
> = {
  high: ChevronsUp,
  normal: Minus,
  low: ChevronDown,
}

const PRIORITIES: TaskPriority[] = ["low", "normal", "high"]

export interface TaskPriorityFieldProps {
  value: TaskPriority
  onValueChange: (next: TaskPriority) => void
  disabled?: boolean
  "data-test"?: string
}

/**
 * Priority, asked as a row of three segments rather than a select.
 *
 * The backlog reads priority as a coloured, shaped badge — the column's whole
 * job is to be scanned — and a form that asked the same question in plain text
 * was teaching two vocabularies for one idea. Each segment carries the same
 * icon and the same hue the badge will wear once the ticket lands, so picking
 * `high` here is already seeing it there.
 *
 * `normal` keeps the badge's deliberate quiet: no hue on the default lane,
 * because a form where the resting state shouts has taught the operator to
 * stop reading the shout.
 *
 * The construction is `TaskSourceCards`' at the compact step — a real
 * `<input type="radio">` off-screen under each segment, so the arrow-key
 * group, the single tab stop and the announced role are the platform's
 * rather than reimplemented. A domain component for the same reason: the
 * kit has no opinion about this product's priority hues.
 */
export function TaskPriorityField({
  value,
  onValueChange,
  disabled = false,
  "data-test": dataTest,
}: TaskPriorityFieldProps) {
  return (
    /* A `span` label rather than a `<legend>`: a legend renders in the
       fieldset's own border slot, outside the layout, so the flex gap
       between it and the segments is whatever the browser's legend rule
       says — not the `s2` every kit field puts between its label and its
       control. The fieldset keeps its group name through `aria-label`,
       which is the same channel the source picker uses. */
    <fieldset
      className={styles.fieldset}
      aria-label="priority"
      data-test={dataTest}
    >
      <span className={styles.fieldLabel}>priority</span>
      <div className={styles.segments}>
        {PRIORITIES.map((priority) => {
          const selected = value === priority
          const Icon = PRIORITY_ICONS[priority]
          return (
            <label
              key={priority}
              className={cn(
                styles.segment,
                styles[priority],
                selected && styles.selected
              )}
              data-test="task-priority-segment"
              data-value={priority}
              data-selected={selected || undefined}
            >
              <input
                type="radio"
                name="task-priority"
                className={styles.input}
                value={priority}
                checked={selected}
                disabled={disabled}
                onChange={() => onValueChange(priority)}
              />
              <Icon className={styles.icon} aria-hidden="true" />
              <span className={styles.segmentLabel}>{priority}</span>
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}