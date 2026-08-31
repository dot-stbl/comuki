import { Zap } from "lucide-react"

import type {
  Task,
  TaskPriority,
  TaskStatus,
} from "@/domains/tasks/model/types"
import { matchesTaskQuery } from "@/domains/tasks/model/filter-tasks"
import {
  TaskPriorityBadge,
  TaskSourceBadge,
  TaskStatusBadge,
} from "@/domains/tasks/ui/tasks-badges"
import {
  can,
  needsLabel,
  projectOf,
  type ProjectRef,
  type Session,
} from "@/shared/session"
import { Button, Tooltip, keySort, rankSort, type DataColumn } from "@/shared/ui"

import styles from "./tasks-table.module.css"

export interface TaskColumnsOptions {
  /** Apps present in the backlog — the `app` filter's options. */
  apps: string[]
  /** Projects present in the backlog — the `project` filter's options. */
  projects: ProjectRef[]
  dispatching: boolean
  onDispatch: (task: Task) => void
  /**
   * The signed-in shift, so the row can answer its own permission.
   *
   * Taking a ticket is a decision inside its project, and the backlog mixes
   * them — so the question is `inbox.take` *on this row's project*, asked per
   * row, never once for the screen. It arrives as the session rather than as a
   * `useCan` below for the reason `RunColumnsOptions` spells out: a `cell` is a
   * function TanStack calls while building a row, not a component, so a hook
   * inside one is a hook outside a render.
   */
  session: Session
}

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getTaskId = (task: Task) => task.id

/** Urgency, not spelling: `high` first, where the alphabet would put it last. */
const PRIORITY_RANK: Record<TaskPriority, number> = {
  high: 0,
  normal: 1,
  low: 2,
}

/** Intake order — how far along the queue a task already is. */
const STATUS_RANK: Record<TaskStatus, number> = {
  planning: 0,
  queued: 1,
  new: 2,
}

/** Units the backlog's pre-formatted `age` speaks, in seconds. */
const AGE_UNITS: Record<string, number | undefined> = {
  s: 1,
  sec: 1,
  secs: 1,
  m: 60,
  min: 60,
  mins: 60,
  h: 3600,
  hr: 3600,
  hrs: 3600,
  d: 86400,
  day: 86400,
  days: 86400,
  w: 604800,
}

/**
 * `age` arrives already formatted — "8 min", "2 h" — so neither the alphabet
 * nor a bare `Number()` orders it: "22 min" is not older than "2 h". The
 * column is `meta.numeric` because it *reads* as a figure, so it says outright
 * how it compares. Anything unparseable sorts last rather than sorting wrong.
 */
function ageSeconds(value: unknown): number {
  const match = /^\s*(\d+(?:[.,]\d+)?)\s*([a-z]+)/i.exec(String(value))
  const unit = match ? AGE_UNITS[match[2].toLowerCase()] : undefined
  return unit === undefined || !match
    ? Number.MAX_SAFE_INTEGER
    : Number(match[1].replace(",", ".")) * unit
}

const prioritySort = rankSort(PRIORITY_RANK)
const statusSort = rankSort(STATUS_RANK)
const ageSort = keySort(ageSeconds)

/**
 * The backlog's column declarations. Filters are declared here, on the column
 * they belong to, so `DataTableToolbar` renders the whole filter bar from this
 * one list — adding a filter is adding a `meta.filter`, not wiring a control.
 *
 * Sorting rides the same declarations. The three columns whose order is a
 * domain fact rather than a string comparison — priority, status, age — name
 * the comparator that knows it; the rest fall back to the table's default.
 */
export function createTaskColumns({
  apps,
  projects,
  dispatching,
  onDispatch,
  session,
}: TaskColumnsOptions): DataColumn<Task>[] {
  return [
    {
      accessorKey: "source",
      header: "source",
      cell: ({ row }) => (
        <TaskSourceBadge source={row.original.source} id={row.original.id} />
      ),
      // Pinned because it is the backlog's identity, not because it is first:
      // for a ticket that came off a branch this badge *is* the id. A manual
      // ticket has only the word "manual" here, which is the honest limit of
      // pinning a column that is a badge — see the note on `title`.
      meta: { width: 136, pinned: true },
    },
    {
      accessorKey: "title",
      header: "task",
      cell: ({ row }) => (
        <span className={styles.title} title={row.original.title}>
          {row.original.title}
        </span>
      ),
      meta: {
        filter: {
          kind: "text",
          placeholder: "filter title, id, app…",
          // The placeholder names three fields, so the predicate reads three
          // fields. Without a `match` the default one compares this column's
          // own `title` and nothing else, and the box quietly promised two
          // searches it never ran — the kind of lie an operator only catches by
          // failing to find a ticket they know is there. `matchesTaskQuery` is
          // the derivation `filterTasks` was already using, named so that the
          // declaration and the screen cannot drift apart a second time.
          match: matchesTaskQuery,
        },
      },
    },
    {
      // The ticket's project, as the key the operator reads — the same column
      // the duty list carries, declared the same way. `id` names the filter
      // value; `accessorKey` names the field the default predicate compares.
      id: "project",
      accessorKey: "projectId",
      header: "project",
      cell: ({ row }) => {
        const project = projectOf(session, row.original.projectId)
        return project ? (
          <span className={styles.project}>{project.key}</span>
        ) : (
          <span className={styles.unknown}>—</span>
        )
      },
      meta: {
        width: 120,
        filter: {
          kind: "select",
          placeholder: "all projects",
          options: projects.map((project) => ({
            value: project.id,
            label: project.key,
          })),
        },
      },
    },
    {
      accessorKey: "app",
      header: "app",
      cell: ({ row }) => (
        <span className={styles.app}>{row.original.app}</span>
      ),
      meta: {
        width: 144,
        filter: {
          kind: "select",
          placeholder: "all apps",
          options: apps.map((app) => ({ value: app, label: app })),
        },
      },
    },
    {
      accessorKey: "priority",
      header: "priority",
      cell: ({ row }) => <TaskPriorityBadge priority={row.original.priority} />,
      sortFn: prioritySort,
      meta: {
        width: 104,
        filter: {
          kind: "select",
          placeholder: "all priority",
          options: [
            { value: "high", label: "high" },
            { value: "normal", label: "normal" },
            { value: "low", label: "low" },
          ],
        },
      },
    },
    {
      accessorKey: "status",
      header: "status",
      cell: ({ row }) => <TaskStatusBadge status={row.original.status} />,
      sortFn: statusSort,
      meta: {
        width: 112,
        filter: {
          kind: "select",
          placeholder: "all status",
          options: [
            { value: "new", label: "new" },
            { value: "queued", label: "queued" },
            { value: "planning", label: "planning" },
          ],
        },
      },
    },
    {
      accessorKey: "age",
      header: "age",
      sortFn: ageSort,
      meta: { width: 88, numeric: true },
    },
    {
      id: "action",
      header: "action",
      // A dispatch button has no order.
      enableSorting: false,
      cell: ({ row }) => {
        const task = row.original
        if (task.status === "planning") {
          return <TaskStatusBadge status="planning" />
        }
        // Asked against the ticket's own project. A backlog that mixes them —
        // and it always does — has rows this shift may hand to the swarm
        // sitting beside rows it may not, and the sentence names which is
        // which rather than reading as a flat no.
        const denial = can(session, "inbox.take", task.projectId)
          ? null
          : needsLabel("inbox.take", projectOf(session, task.projectId)?.key)
        return (
          // Forty rows of a button called "Dispatch" name nothing, so the
          // ticket rides in the accessible name and the bolt carries the act.
          // The tooltip hands the word back on hover and on focus; a refused
          // row puts its sentence there instead.
          <Tooltip content={denial ?? "Dispatch"}>
            <Button
              variant="ghost"
              size="icon-sm"
              data-test="task-dispatch"
              disabled={dispatching}
              denied={denial}
              aria-label={`Dispatch ${task.title}`}
              onClick={(event) => {
                event.stopPropagation()
                onDispatch(task)
              }}
            >
              {/* No width of its own: a kit button sizes the svg inside it per
                  size variant, and handing one an explicit width makes the icon
                  stop tracking the control it rides. */}
              <Zap aria-hidden="true" />
            </Button>
          </Tooltip>
        )
      },
      // Wide enough for the "planning" pill this cell shows instead of a
      // button on a row already taken; the glyph alone would fit in half of it.
      meta: { width: 96, align: "end", label: "action" },
    },
  ]
}
