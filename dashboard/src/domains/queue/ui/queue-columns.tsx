import { Link } from "@tanstack/react-router"

import { PROFILE_CATALOG } from "@/shared/api/mock/runs.seed"
import type { ProjectRef } from "@/shared/session"
import { rankSort, type DataColumn } from "@/shared/ui"

import { QUEUE_RANK, WORK_ITEM_STATUSES } from "@/domains/queue/model/queue"
import type { QueueItem } from "@/domains/queue/model/types"

import { AgeMeter } from "./meters"
import { WorkStatusBadge } from "./queue-badges"
import styles from "./queue-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getQueueItemId = (item: QueueItem) => item.id

/**
 * Sorting the status column sorts by triage, not by spelling — the same rank
 * `queueOrder` reads, so the head and the list's own opening order can never
 * disagree about which end is the worrying one.
 */
const statusSort = rankSort(QUEUE_RANK)

export interface QueueColumnsOptions {
  /** Projects the session can see — the `project` filter's options and names. */
  projects: ProjectRef[]
}

/**
 * The queue's column declarations.
 *
 * Filters are declared on the columns they belong to, so `DataTableToolbar`
 * assembles the bar and `applyDataFilters` evaluates it from this one list.
 * The profile filter is the important one: profile is the axis a worker claims
 * on, so narrowing both halves of the screen to a profile is how an operator
 * asks "is anything actually able to pick this up?".
 *
 * Profile options come from `PROFILE_CATALOG` rather than from the rows,
 * because the catalog is closed — it is what the client declared in git, and a
 * profile with nothing queued on it is a real answer, not an absence.
 *
 * Nothing here needs the session: a work item's row carries no act, so no
 * permission is resolved per row on this half. The worker half is where that
 * lives, and it takes the session as an option for exactly that reason.
 */
export function createQueueColumns({
  projects,
}: QueueColumnsOptions): DataColumn<QueueItem>[] {
  const projectName = new Map(projects.map((entry) => [entry.id, entry.key]))

  return [
    {
      accessorKey: "status",
      header: "status",
      cell: ({ row }) => <WorkStatusBadge status={row.original.status} />,
      sortFn: statusSort,
      meta: {
        width: 116,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all statuses",
          options: WORK_ITEM_STATUSES.map((status) => ({
            value: status,
            label: status,
          })),
        },
      },
    },
    {
      accessorKey: "id",
      header: "item",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.id}</span>
      ),
      meta: { width: 88, pinned: true },
    },
    {
      accessorKey: "runId",
      header: "run",
      cell: ({ row }) => (
        <Link
          to="/runs/$runId"
          params={{ runId: row.original.runId }}
          className={styles.link}
          data-test="queue-run-link"
        >
          {row.original.runId}
        </Link>
      ),
      meta: { width: 104, label: "run" },
    },
    {
      accessorKey: "projectId",
      header: "project",
      // The key, not the display name: it is the handle an operator types and
      // reads, and it is what the denial sentence on the other half names.
      cell: ({ row }) => (
        <span className={styles.value}>
          {projectName.get(row.original.projectId) ?? row.original.projectId}
        </span>
      ),
      meta: {
        width: 104,
        label: "project",
        filter: {
          kind: "select",
          placeholder: "all projects",
          options: projects.map((entry) => ({
            value: entry.id,
            label: entry.key,
          })),
        },
      },
    },
    {
      accessorKey: "profile",
      header: "profile",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.profile}</span>
      ),
      meta: {
        width: 116,
        filter: {
          kind: "select",
          placeholder: "all profiles",
          options: PROFILE_CATALOG.map((profile) => ({
            value: profile,
            label: profile,
          })),
        },
      },
    },
    {
      accessorKey: "label",
      header: "step",
      cell: ({ row }) => (
        <span className={styles.step} title={row.original.label}>
          {row.original.label}
        </span>
      ),
      meta: {
        label: "step",
        filter: {
          kind: "text",
          placeholder: "filter item, run, step…",
          /* The project key is in the haystack and deliberately *not* in the
             placeholder, and both halves of that are decisions.

             It is in the haystack because a destination that cannot receive
             what it is sent lands the operator on an empty screen — the
             contract written out at the top of `app/search/shapes.ts`. The
             project detail page hands its queue off as `/queue?q=<slug>`, and
             `?q=` is this filter. Without the key here that link resolves to a
             screen that says "nothing queued" about a project with fourteen
             items on it, which is worse than not offering the link at all.

             It is not in the placeholder because the key already has a column
             of its own and a select filter of its own, and this box is for the
             words on the row — advertising a third way to narrow by project
             would teach the operator to type where they should be picking. */
          match: (item, needle) =>
            `${item.id} ${item.runId} ${item.label} ${item.profile} ${projectName.get(item.projectId) ?? ""}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "claimedBy",
      header: "claimed by",
      // The lease is released the moment an item stops running, so most rows
      // have no claimant — and a dash says that better than a blank cell,
      // which reads as a rendering fault.
      cell: ({ row }) =>
        row.original.claimedBy ? (
          <span className={styles.value}>{row.original.claimedBy}</span>
        ) : (
          <span className={styles.faint}>—</span>
        ),
      meta: { width: 104, label: "claimed by" },
    },
    {
      accessorKey: "ageSec",
      header: "age",
      cell: ({ row }) => <AgeMeter item={row.original} />,
      meta: { width: 96, numeric: true, label: "age" },
    },
  ]
}
