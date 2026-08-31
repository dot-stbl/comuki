import { Loader2, Play } from "lucide-react"

import { headroom } from "@/domains/compute/model/capacity"
import type {
  ComputePool,
  ComputeProvider,
  ProviderState,
} from "@/domains/compute/model/types"
import { can, needsLabel, type Session } from "@/shared/session"
import { Button, Tooltip, rankSort, type DataColumn } from "@/shared/ui"

import { ProviderKindMark, ProviderStateBadge } from "./compute-badges"
import styles from "./compute-table.module.css"

export interface ProviderColumnsOptions {
  pools: ComputePool[]
  /** The provider a switch is currently in flight for. */
  switchingId: string | null
  onTakeWork: (provider: ComputeProvider) => void
  /**
   * The signed-in shift itself, not an answer about it.
   *
   * `compute.manage` is a *platform* permission — it reads platform roles and
   * nothing else, so unlike the duty list there is no project to ask about and
   * no `projectId` to pass. It still has to arrive as the session rather than
   * as a computed `PermissionCheck`, because TanStack calls `cell` as a plain
   * function while it builds a row: a `useCan` in there is a hook called
   * outside a render, which typechecks and then throws. `can()` and
   * `needsLabel()` are the same rules as plain functions, which is why they are
   * exported beside the hook.
   */
  session: Session
}

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getProviderId = (provider: ComputeProvider) => provider.id

const PROVIDER_STATES: ProviderState[] = [
  "active",
  "draining",
  "standby",
  "unreachable",
]

/**
 * Provider states in the order an operator wants to find them: the one that is
 * broken first, then the one taking work, then the rest. Alphabetically this
 * reads "active, draining, standby, unreachable", which is no order at all.
 */
const stateSort = rankSort({
  unreachable: 0,
  active: 1,
  draining: 2,
  standby: 3,
})

/**
 * The provider registry's columns.
 *
 * A registry row answers four questions and no more: which implementation is
 * this, what does the orchestrator dial, is it taking work, and how much room
 * does its capacity API say is left. Everything else about compute is a
 * property of a *pool* rather than of a provider, and lives on the cards below.
 *
 * Filters are declared here, on the column they belong to, so `DataTableToolbar`
 * renders the bar and `applyDataFilters` evaluates it from this one list.
 */
export function createProviderColumns({
  pools,
  switchingId,
  onTakeWork,
  session,
}: ProviderColumnsOptions): DataColumn<ComputeProvider>[] {
  const poolCount = (providerId: string) =>
    pools.filter((pool) => pool.providerId === providerId).length

  const workerCount = (providerId: string) =>
    pools
      .filter((pool) => pool.providerId === providerId)
      .reduce((total, pool) => total + pool.workers, 0)

  return [
    {
      accessorKey: "kind",
      header: "kind",
      // The backend's own mark, not its name spelled out. The word is still the
      // filter's option, the mark's accessible name and its hover reading — it
      // has stopped being the thing occupying the column.
      cell: ({ row }) => <ProviderKindMark kind={row.original.kind} />,
      meta: {
        width: 88,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all kinds",
          options: [
            { value: "docker", label: "docker" },
            { value: "kubernetes", label: "kubernetes" },
          ],
        },
      },
    },
    {
      accessorKey: "endpoint",
      header: "endpoint",
      cell: ({ row }) => (
        <span className={styles.endpoint} title={row.original.endpoint}>
          {row.original.endpoint}
        </span>
      ),
      meta: {
        width: 300,
        filter: {
          kind: "text",
          placeholder: "filter endpoint, note…",
          match: (provider, needle) =>
            `${provider.endpoint} ${provider.note} ${provider.id}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "state",
      header: "state",
      cell: ({ row }) => <ProviderStateBadge state={row.original.state} />,
      sortFn: stateSort,
      meta: {
        width: 128,
        filter: {
          kind: "select",
          placeholder: "all states",
          options: PROVIDER_STATES.map((state) => ({
            value: state,
            label: state,
          })),
        },
      },
    },
    {
      id: "takingWork",
      accessorFn: (provider) => (provider.takingWork ? "yes" : "no"),
      header: "new starts",
      // The single most consequential fact on the row, and it is a yes or a no
      // rather than a badge: a second badge beside `state` would read as a
      // second state, and there is only one.
      cell: ({ row }) =>
        row.original.takingWork ? (
          <span className={styles.target}>
            <Play className={styles.targetIcon} aria-hidden="true" />
            taking work
          </span>
        ) : (
          <span className={styles.faint}>—</span>
        ),
      meta: { width: 128, label: "new starts" },
    },
    {
      id: "pools",
      accessorFn: (provider) => poolCount(provider.id),
      header: "pools",
      cell: ({ row }) => (
        <span className={styles.value}>{poolCount(row.original.id)}</span>
      ),
      meta: { width: 80, numeric: true },
    },
    {
      id: "workers",
      accessorFn: (provider) => workerCount(provider.id),
      header: "workers",
      cell: ({ row }) => (
        <span className={styles.value}>{workerCount(row.original.id)}</span>
      ),
      meta: { width: 88, numeric: true },
    },
    {
      id: "allocatable",
      accessorFn: (provider) =>
        provider.allocatable ? headroom(provider.allocatable) : -1,
      header: "allocatable",
      // A provider that did not answer has no capacity reading, and `0` would
      // be a lie in the shape of a number: it reads as a full cluster, which is
      // an entirely different thing an operator would act on differently.
      cell: ({ row }) => {
        const allocatable = row.original.allocatable
        if (!allocatable) {
          return <span className={styles.faint}>no answer</span>
        }
        return (
          <span className={styles.value}>
            {allocatable.used} / {allocatable.limit}
          </span>
        )
      },
      meta: { width: 112, numeric: true, label: "allocatable" },
    },
    {
      accessorKey: "note",
      header: "note",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.note}>
          {row.original.note}
        </span>
      ),
      meta: { label: "note" },
    },
    {
      id: "actions",
      header: "actions",
      enableSorting: false,
      cell: ({ row }) => {
        const provider = row.original
        // Nothing to offer: this one already takes the work, or it cannot be
        // reached to be handed any. An absent control is honest here — the act
        // does not exist for this row, which is not the same as a role being
        // refused it, and only the second one owes an explanation.
        if (provider.takingWork || provider.state === "unreachable") {
          return null
        }

        const busy = switchingId === provider.id
        // Platform permission: asked without a project, because project roles
        // must never open a platform act. A denied role still sees the button
        // in the same place at the same size — `denied` swaps the tooltip for
        // the sentence naming the role that would work and refuses the click.
        const denial = can(session, "compute.manage")
          ? null
          : needsLabel("compute.manage")

        return (
          <span className={styles.actions}>
            {/* The same glyph the `new starts` column uses for a provider
                that is already taking work — the act and the state it
                produces are one object, and they never appear on the same
                row because this cell is empty once the state is true. */}
            <Tooltip content={denial ?? "Hand new starts to this provider"}>
              <Button
                size="icon-sm"
                variant="outline"
                data-test="provider-take-work"
                disabled={busy}
                denied={denial}
                aria-busy={busy || undefined}
                aria-label={`Hand new starts to ${provider.endpoint}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onTakeWork(provider)
                }}
              >
                {busy ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <Play aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 80, align: "end", label: "actions" },
    },
  ]
}
