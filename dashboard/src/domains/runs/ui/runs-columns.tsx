import { Check, Loader2, X } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { formatCost, formatDuration } from "@/domains/runs/model/format"
import { TRIAGE_RANK } from "@/domains/runs/model/profile-flow"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"
import { currentLabel, currentProfile } from "@/domains/runs/model/work-items"
import {
  can,
  needsLabel,
  projectOf,
  type ProjectRef,
  type Session,
} from "@/shared/session"
import {
  Button,
  StatusBadge,
  Tooltip,
  rankSort,
  type DataColumn,
} from "@/shared/ui"

import styles from "./runs-table.module.css"

export interface RunColumnsOptions {
  /** Apps present in the run list — the `app` filter's options. */
  apps: string[]
  /** Projects present in the run list — the `project` filter's options. */
  projects: ProjectRef[]
  /** Profiles in board order — the `profile` filter's options and its rank. */
  profiles: string[]
  approvingId: string | null
  cancellingId: string | null
  onApprove: (run: RunSummary) => void
  onCancel: (run: RunSummary) => void
  /**
   * The signed-in shift itself, not an answer about it.
   *
   * A `PermissionCheck` computed once by the page used to ride down here, and
   * it was the wrong shape: it answered for the *session* when the question is
   * about the *row*. The same person is approver on one project and viewer on
   * the next, so a single yes-or-no for the whole list is wrong on half of it.
   *
   * It has to be the session rather than a `useCan` in the cell because a
   * `cell` is not a component: TanStack calls it as a plain function while it
   * builds a row, so a hook inside one is a hook called outside a render, which
   * typechecks and then throws. `can()` and `needsLabel()` are the same rules
   * as plain functions, which is why they are exported beside the hook.
   */
  session: Session
}

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getRunId = (run: RunSummary) => run.id

/** The six statuses the whole product speaks: DB, contract, mocks, tokens. */
const RUN_STATUSES: RunStatus[] = [
  "running",
  "waiting",
  "queued",
  "escalated",
  "failed",
  "success",
]

/**
 * Sorting the status column means sorting by triage, not by spelling: the
 * rank comes from the same constant `triageOrder` reads, so the header and
 * the screen's default order can never disagree about what "worst" is.
 */
const statusSort = rankSort(TRIAGE_RANK)

/** A run whose next move belongs to a person, not to the swarm. */
function needsHuman(run: RunSummary): boolean {
  return run.status === "waiting" || run.status === "escalated"
}

/**
 * The duty list's column declarations. Filters are declared here, on the column
 * they belong to, so `DataTableToolbar` renders the whole filter bar and
 * `applyDataFilters` evaluates it from this one list. That is what makes the
 * flow board an ordinary filter: clicking a profile writes the `profile` value,
 * and it shows up in the toolbar exactly like a value the user typed.
 *
 * Two of the row's columns come off the run's current work item, and they are
 * emphatically not the same kind of thing. `profile` is the closed catalog key
 * — filterable as a select, sortable by the pipeline's own order. `step` is the
 * name the brain invented for this ticket — prose, shown because it is the most
 * human thing on the row, and never used as a key by anything.
 *
 * Sorting reads the same declarations: `meta.numeric` already tells the table
 * that in-step time and cost are numbers, so only the two columns whose order
 * is a domain fact rather than a string comparison — status and profile — spell
 * a comparator out.
 *
 * `meta.pinned` covers status *and* run. Pinning is a flag rather than a
 * position precisely so this list gets to answer "which row am I on" while it
 * is scrolled sideways, and status alone does not answer that — a badge with no
 * id beside it says what is happening to a run nobody can name. If only one of
 * the two can stay, it is `run`: drop the flag from status, not from the id.
 * `meta.width` is the width both open at; either can be dragged from there.
 */
export function createRunColumns({
  apps,
  projects,
  profiles,
  approvingId,
  cancellingId,
  onApprove,
  onCancel,
  session,
}: RunColumnsOptions): DataColumn<RunSummary>[] {
  // Profiles arrive in the order the board derived from the observed graphs,
  // which is the only order they have — sorted alphabetically the pipeline
  // reads "docs, explorer, implementer, planner".
  const profileSort = rankSort(
    Object.fromEntries(profiles.map((profile, index) => [profile, index]))
  )

  return [
    {
      accessorKey: "status",
      header: "status",
      cell: ({ row }) => <StatusBadge status={row.original.status} size="sm" />,
      sortFn: statusSort,
      meta: {
        width: 120,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all statuses",
          options: RUN_STATUSES.map((status) => ({
            value: status,
            label: status,
          })),
        },
      },
    },
    {
      accessorKey: "id",
      header: "run",
      cell: ({ row }) => (
        <Link
          to="/runs/$runId"
          params={{ runId: row.original.id }}
          className={styles.id}
          data-test="run-link"
        >
          {row.original.id}
        </Link>
      ),
      meta: { width: 104, pinned: true },
    },
    {
      // The row's project, shown as the key the operator types and reads. The
      // id is plumbing and the display name is prose; the key is the value, so
      // it sits in the data voice beside `app` and filters the same way.
      // `id` and `accessorKey` both, deliberately: the toolbar and the filter
      // value key read the id (`project`, the word), while the default select
      // predicate reads the field (`projectId`, what the row carries).
      id: "project",
      accessorKey: "projectId",
      header: "project",
      cell: ({ row }) => {
        const project = projectOf(session, row.original.projectId)
        // A run in a project this session cannot see still has to render as a
        // row — the honest dash says the swarm is working somewhere out of
        // view rather than leaving a blank that reads as a broken cell.
        return project ? (
          <span className={styles.project}>{project.key}</span>
        ) : (
          <span className={styles.unplanned}>—</span>
        )
      },
      meta: {
        width: 120,
        filter: {
          kind: "select",
          placeholder: "all projects",
          // Value is the id, because that is what the row carries; the label
          // is the key, because that is what the operator knows it by.
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
      cell: ({ row }) => <span className={styles.app}>{row.original.app}</span>,
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
      accessorKey: "title",
      header: "task",
      cell: ({ row }) => (
        <span className={styles.title} title={row.original.title}>
          {row.original.title}
        </span>
      ),
      meta: {
        label: "task",
        filter: {
          kind: "text",
          placeholder: "filter run, task, step…",
          // The project key is in the haystack and not in the placeholder, and
          // both halves of that are deliberate. A project's own screen hands
          // its runs off as `/runs?q=<slug>` — the same `q` the palette writes
          // — and a destination that cannot receive what it is sent lands the
          // operator on an empty list, which is worse than not offering the
          // link. It is not advertised because the key already has a column
          // *and* a select filter beside it: the text box is for the words on
          // the row, and this is the seam that makes a hand-off land.
          match: (run, needle) =>
            `${run.id} ${run.title} ${run.app} ${currentLabel(run)} ${
              projectOf(session, run.projectId)?.key ?? ""
            }`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      id: "profile",
      accessorFn: currentProfile,
      header: "profile",
      sortFn: profileSort,
      // A ticket the brain has not planned yet has no profile to stand on —
      // an honest dash beats a blank cell that reads as a rendering fault.
      cell: ({ row }) => {
        const profile = currentProfile(row.original)
        return profile ? (
          <span className={styles.profile}>{profile}</span>
        ) : (
          <span className={styles.unplanned}>—</span>
        )
      },
      meta: {
        width: 120,
        label: "profile",
        filter: {
          kind: "select",
          placeholder: "all profiles",
          options: profiles.map((profile) => ({
            value: profile,
            label: profile,
          })),
          match: (run, value) => currentProfile(run) === value,
        },
      },
    },
    {
      id: "step",
      accessorFn: currentLabel,
      header: "step",
      cell: ({ row }) => {
        const label = currentLabel(row.original)
        return label ? (
          <span className={styles.step} title={label}>
            {label}
          </span>
        ) : (
          <span className={styles.unplanned}>waiting on a plan</span>
        )
      },
      meta: { label: "step" },
    },
    {
      accessorKey: "durationSec",
      header: "in step",
      cell: ({ row }) => formatDuration(row.original.durationSec),
      meta: { width: 96, numeric: true, label: "in step" },
    },
    {
      accessorKey: "cost",
      header: "cost",
      cell: ({ row }) => formatCost(row.original.cost),
      meta: { width: 88, numeric: true },
    },
    {
      accessorKey: "model",
      header: "worker",
      cell: ({ row }) => (
        <span className={styles.worker}>{row.original.model}</span>
      ),
      meta: { width: 96, label: "worker" },
    },
    {
      id: "actions",
      header: "actions",
      // Two buttons and a blank have no order. Say so rather than leaning on
      // the fact that a column without an accessor happens not to sort.
      enableSorting: false,
      cell: ({ row }) => {
        const run = row.original
        if (!needsHuman(run)) {
          return null
        }
        const approving = approvingId === run.id
        const busy = approving || cancellingId === run.id

        // The decision is the row's, so the permission is the row's. Asked
        // against `run.projectId` rather than against the shift: the same
        // person approves on one project and only watches the next, and a
        // list that mixes projects — which is every list here — would
        // otherwise answer for the wrong one on half its rows.
        //
        // The key, not the id and not the display name, is what the sentence
        // names: it is what the operator calls the project, and it is what the
        // `project` column two cells to the left is already showing them.
        const where = projectOf(session, run.projectId)?.key
        const approveDenial = can(session, "plans.approve", run.projectId)
          ? null
          : needsLabel("plans.approve", where)
        const stopDenial = can(session, "runs.stop", run.projectId)
          ? null
          : needsLabel("runs.stop", where)

        // Icon-only in a dense row, so the two flexible columns keep their
        // width. An icon with no name is a guess, so each carries both an
        // `aria-label` (assistive tech) and the kit tooltip, which hands the
        // word back on hover *and* on focus — which the `title` attribute it
        // replaces never did. The pending state is spoken through `aria-busy`
        // rather than by swapping the label to "Approving…", which no longer
        // has room.
        //
        // A role that may not decide still sees both buttons, in the same
        // place, at the same size: `denied` swaps the tooltip for the sentence
        // naming the role that would work — and the project it would work on —
        // and refuses the click. Hiding them would make the row a different
        // shape per viewer and teach nobody what to ask for. The two acts are
        // gated apart because they are apart in the matrix — a member stops a
        // run and cannot approve a plan — so the common case is one live
        // button beside one explained one, and now that can differ row to row.
        return (
          <span className={styles.actions}>
            <Tooltip content={approveDenial ?? "Approve"}>
              <Button
                size="icon-sm"
                data-test="run-approve"
                disabled={busy}
                denied={approveDenial}
                aria-busy={approving || undefined}
                aria-label={`Approve ${run.title}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onApprove(run)
                }}
              >
                {approving ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <Check aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
            <Tooltip content={stopDenial ?? "Cancel run"}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="run-cancel"
                disabled={busy}
                denied={stopDenial}
                aria-label={`Cancel ${run.title}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onCancel(run)
                }}
              >
                <X aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 80, align: "end", label: "actions" },
    },
  ]
}
