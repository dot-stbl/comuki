import { Link } from "@tanstack/react-router"
import { Loader2, LogOut, PowerOff } from "lucide-react"

import { PROFILE_CATALOG } from "@/shared/api/mock/runs.seed"
import {
  can,
  needsLabel,
  projectOf,
  type ProjectRef,
  type Session,
} from "@/shared/session"
import {
  Button,
  Tooltip,
  keySort,
  rankSort,
  type DataColumn,
} from "@/shared/ui"

import { formatDuration } from "@/domains/runs/model/format"
import { WORKER_STATES } from "@/domains/queue/model/queue"
import type { QueueItem, Worker } from "@/domains/queue/model/types"

import { LeaseMeter } from "./meters"
import { WorkerStateBadge } from "./queue-badges"
import styles from "./queue-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getWorkerId = (worker: Worker) => worker.id

/** Capacity first: what is working, what is leaving, what is spare. */
const STATE_RANK: Record<string, number> = {
  draining: 0,
  busy: 1,
  idle: 2,
}

const stateSort = rankSort(STATE_RANK)

/** A worker with no lease sorts after every worker that has one. */
const leaseSort = keySort((value) =>
  value === null || value === undefined
    ? Number.MAX_SAFE_INTEGER
    : Number(value)
)

export interface WorkerColumnsOptions {
  /** Projects the session can see — the `project` filter's options and names. */
  projects: ProjectRef[]
  /** Work items by id, so a busy worker can name what it is holding. */
  itemsById: Map<string, QueueItem>
  /**
   * The signed-in shift, handed down as a value.
   *
   * A `cell` is not a component: TanStack calls it as a plain function while
   * it builds a row, so a `useCan` inside one is a hook called outside a
   * render — it typechecks and then throws. Both acts here are gated on the
   * *row's* project rather than on the screen, so the answer cannot be hoisted
   * into a single `useCan` at the top either. The session comes down instead,
   * and the cells ask `can` and `needsLabel` directly, which are plain
   * functions over it.
   */
  session: Session
  /** Worker id whose drain is in flight, if any. */
  drainingId: string | null
  /** Worker id whose force stop is in flight, if any. */
  stoppingId: string | null
  onDrain: (worker: Worker) => void
  onForceStop: (worker: Worker) => void
}

/**
 * The pool's column declarations.
 *
 * The row is a container: what it is, what it is running, where it runs and
 * how much of its lease is left. The two acts at the end are not two
 * intensities of one thing — drain is lossless and force stop is not — so they
 * are two buttons rather than a menu, and only one of them asks first.
 */
export function createWorkerColumns({
  projects,
  itemsById,
  session,
  drainingId,
  stoppingId,
  onDrain,
  onForceStop,
}: WorkerColumnsOptions): DataColumn<Worker>[] {
  const projectName = new Map(projects.map((entry) => [entry.id, entry.key]))

  return [
    {
      accessorKey: "state",
      header: "state",
      cell: ({ row }) => <WorkerStateBadge state={row.original.state} />,
      sortFn: stateSort,
      meta: {
        width: 116,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all states",
          options: WORKER_STATES.map((state) => ({
            value: state,
            label: state,
          })),
        },
      },
    },
    {
      accessorKey: "id",
      header: "worker",
      /* The identifier is the link, exactly as the duty list's run id is —
         and emphatically *not* the whole row. A row-wide click target would
         swallow the two acts at the other end of it: drain and force stop sit
         in the actions column, and a person aiming at force stop who landed on
         the worker's page instead would have to find their way back and try
         again on a row that had moved. One cell, one destination. */
      cell: ({ row }) => (
        <Link
          to="/queue/workers/$workerId"
          params={{ workerId: row.original.id }}
          className={styles.link}
          data-test="worker-link"
        >
          {row.original.id}
        </Link>
      ),
      meta: {
        width: 96,
        pinned: true,
        label: "worker",
        // One text box over everything that identifies a container: the pool
        // is eleven rows today and a hundred when a project scales, and by
        // then "which one is on the old digest" is the question being asked.
        filter: {
          kind: "text",
          placeholder: "filter worker, handle, image…",
          /* The project key rides in the haystack for the same reason it does
             on the queue half: a hand-off has to be receivable. `?w=` is where
             `shapes.ts` sends a worker id and an image digest, and a person
             who narrowed the pool to one project by typing its handle rather
             than by opening the select should not be told the pool is empty.
             Resolved through `projectOf` — the same lookup the denial sentence
             at the end of this row already uses, so the two cannot disagree
             about what a project is called.

             Not advertised in the placeholder: project has its own column and
             its own select filter here too, and this box names containers. */
          match: (worker, needle) =>
            `${worker.id} ${worker.handle} ${worker.digest} ${projectOf(session, worker.projectId)?.key ?? ""}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "projectId",
      header: "project",
      cell: ({ row }) => (
        <span className={styles.value}>
          {projectName.get(row.original.projectId) ?? row.original.projectId}
        </span>
      ),
      meta: {
        width: 100,
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
      id: "current",
      accessorFn: (worker) =>
        worker.itemId ? (itemsById.get(worker.itemId)?.label ?? worker.itemId) : "",
      header: "current work",
      cell: ({ row }) => {
        const worker = row.original
        const item = worker.itemId ? itemsById.get(worker.itemId) : undefined
        if (!item) {
          // Idle is the honest answer, and it is not a gap: an idle worker is
          // the pool doing its job.
          return <span className={styles.faint}>idle</span>
        }
        return (
          <span className={styles.current}>
            <Link
              to="/runs/$runId"
              params={{ runId: item.runId }}
              className={styles.link}
              data-test="worker-run-link"
            >
              {item.runId}
            </Link>
            <span className={styles.step} title={item.label}>
              {item.label}
            </span>
          </span>
        )
      },
      meta: { label: "current work" },
    },
    {
      accessorKey: "provider",
      header: "compute",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.provider}</span>
      ),
      meta: {
        width: 108,
        label: "compute",
        filter: {
          kind: "select",
          placeholder: "all providers",
          options: [
            { value: "docker", label: "docker" },
            { value: "kubernetes", label: "kubernetes" },
          ],
        },
      },
    },
    {
      accessorKey: "handle",
      header: "handle",
      cell: ({ row }) => (
        <span className={styles.value} title={row.original.handle}>
          {row.original.handle}
        </span>
      ),
      meta: { width: 240, label: "handle" },
    },
    {
      // Off by default and one click away in the column manager. It is the
      // answer to "why is that one draining" — a claim only matches a worker
      // whose image digest is current — and it is the wrong thing to spend a
      // column on until somebody is asking.
      accessorKey: "digest",
      header: "image",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.digest}</span>
      ),
      meta: { width: 132, label: "image" },
    },
    {
      accessorKey: "leaseSec",
      header: "lease",
      cell: ({ row }) => <LeaseMeter worker={row.original} />,
      sortFn: leaseSort,
      meta: { width: 96, align: "end", label: "lease" },
    },
    {
      accessorKey: "upSec",
      header: "up",
      cell: ({ row }) => formatDuration(row.original.upSec),
      meta: { width: 88, numeric: true, label: "up" },
    },
    {
      id: "actions",
      header: "actions",
      // Two buttons and nothing to compare. Say so rather than relying on a
      // column without an accessor happening not to sort.
      enableSorting: false,
      cell: ({ row }) => {
        const worker = row.original
        // Resolved per row, because permission is: the same person administers
        // one project's pool and can only watch the next one's.
        const allowed = can(session, "runs.stop", worker.projectId)
        const denial = allowed
          ? null
          : needsLabel(
              "runs.stop",
              projectOf(session, worker.projectId)?.key
            )

        const draining = drainingId === worker.id
        const stopping = stoppingId === worker.id
        const busy = draining || stopping

        return (
          <span className={styles.actions}>
            {/* The kit tooltip rather than a native `title`: it arrives on
                focus as well as on hover, and a refused act puts its sentence
                in the same place the word would have been. */}
            <Tooltip content={denial ?? "Drain"}>
              <Button
                variant="outline"
                size="icon-sm"
                data-test="worker-drain"
                // `disabled` is for busy and invalid — a worker already leaving
                // has nothing to drain. Denial is a different thing and takes
                // `denied`, which keeps the control reachable so its sentence
                // is.
                disabled={busy || worker.state === "draining"}
                denied={denial}
                aria-busy={draining || undefined}
                aria-label={`Drain ${worker.id}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onDrain(worker)
                }}
              >
                {draining ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <LogOut aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
            <Tooltip content={denial ?? "Force stop"}>
              <Button
                variant="destructive"
                size="icon-sm"
                data-test="worker-force-stop"
                disabled={busy}
                denied={denial}
                aria-busy={stopping || undefined}
                aria-label={`Force stop ${worker.id}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onForceStop(worker)
                }}
              >
                <PowerOff aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 84, align: "end", label: "actions" },
    },
  ]
}
