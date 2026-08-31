import type { RunStatus, RunSummary } from "@/domains/runs/model/types"
import { currentItem, itemDepths } from "@/domains/runs/model/work-items"

/**
 * Profile flow — the aggregate the duty board reads.
 *
 * The board's axis is the **worker profile**, because that is the only axis a
 * plan has that is stable across runs and actionable when it jams: profiles are
 * a closed catalog declared in the client's git, and a profile that is backing
 * up is fixed by editing that profile. The brain-invented step name is not an
 * axis — it is prose, different on every ticket, and aggregating on it would
 * turn six real things into forty columns.
 *
 * Nothing here is assumed about the shape of a plan. The columns, their order
 * and the transitions between them are all measured from the graphs the runs
 * actually have:
 *
 * - **nodes** are the profiles observed, with the same pool / cleared /
 *   entered / blocked reading the board has always drawn. A node counts *runs*,
 *   not work items: one plan may invoke `implementer` eight times, and a board
 *   where a channel is eight times its neighbours because of that measures the
 *   shape of plans rather than the state of the shift. A run is in a profile's
 *   pool when the item it is standing on belongs to that profile — the same
 *   test the list's `profile` filter applies, so the number under a channel and
 *   the rows behind it are the same runs.
 * - **edges** are observed transitions, and they are item-level: an item of
 *   profile A finished and fed an item of profile B. Counted, not assumed.
 *   `crossings` folds them back to runs so the connectors stay on one axis
 *   with the channels they join.
 * - **column order** is each profile's median depth across those graphs, so
 *   the board shows the pipeline the project has rather than one we hardcoded.
 *
 * The orchestrator has no `profile x status` endpoint yet — `/swarm` returns
 * run counts by status with no profile axis and no transitions — so this
 * derives it from the run list, which is correct for the mock set and for any
 * page size the client already holds. When the endpoint lands, replace the call
 * site in `api/queries.ts`; the shape below is what the screen needs from it.
 */

export interface ProfileFlowNode {
  /** Catalog key of the profile. The node's identity — never a brain label. */
  profile: string
  /** Runs standing on this profile right now. */
  pool: number
  poolByStatus: Record<RunStatus, number>
  /**
   * Runs that already cleared this profile — the throughput the board draws.
   * A run counted in `pool` is not counted here as well, even when part of its
   * work on this profile is done: it is still standing on it.
   */
  cleared: number
  /** Runs that reached this profile at all: pool + cleared. */
  entered: number
  /** Pool that cannot move on its own: waiting, escalated, failed. */
  blocked: number
  /** Median depth of this profile across the observed graphs. Sets the column. */
  depth: number
}

/** One observed transition, counted: `from` finished and fed `to`. */
export interface ProfileFlowEdge {
  from: string
  to: string
  count: number
}

export interface ProfileFlowColumn {
  /** The median depth every profile in this column shares. */
  depth: number
  /** More than one profile sits at this depth — the pipeline branches here. */
  parallel: boolean
  nodes: ProfileFlowNode[]
}

export interface ProfileFlow {
  columns: ProfileFlowColumn[]
  /** Profiles flat, in board order. The rank the table sorts `profile` by. */
  order: string[]
  edges: ProfileFlowEdge[]
  /**
   * Runs crossing each gap between columns, `columns.length - 1` long. A run is
   * counted in `crossings[i]` when one of its observed transitions leaves
   * column `i` or earlier and lands past it — the flow through that gap,
   * measured rather than assumed, and in the channels' own unit so a connector
   * and the columns it joins are drawn against one scale.
   */
  crossings: number[]
  /** Largest `entered` across profiles — the scale every band is drawn against. */
  scale: number
  /** Profile holding the most blocked items: the pinch the board points at. */
  pinchProfile: string | null
  /** Runs whose next move belongs to a person. */
  blockedTotal: number
  /** Runs actively moving. */
  runningTotal: number
  total: number
}

const EMPTY_STATUS: Record<RunStatus, number> = {
  running: 0,
  success: 0,
  failed: 0,
  waiting: 0,
  queued: 0,
  escalated: 0,
}

const BLOCKING: RunStatus[] = ["waiting", "escalated", "failed"]

const EMPTY_FLOW: ProfileFlow = {
  columns: [],
  order: [],
  edges: [],
  crossings: [],
  scale: 0,
  pinchProfile: null,
  blockedTotal: 0,
  runningTotal: 0,
  total: 0,
}

/**
 * Lower median, so the value is one of the depths actually observed and stays
 * a whole number: profiles that genuinely sit at the same depth then land in
 * the same column instead of being split apart by a half.
 */
function median(values: number[]): number {
  if (values.length === 0) {
    return 0
  }
  const sorted = [...values].sort((a, b) => a - b)
  return sorted[Math.floor((sorted.length - 1) / 2)]
}

interface Accumulator extends Omit<ProfileFlowNode, "depth"> {
  depths: number[]
}

export function buildProfileFlow(runs: RunSummary[]): ProfileFlow {
  if (runs.length === 0) {
    return EMPTY_FLOW
  }

  const accumulators = new Map<string, Accumulator>()
  // Keyed by the pair but carrying it too, so the key is only ever a key: a
  // separator that has to be split back apart is a profile name away from a bug.
  const edgeCounts = new Map<string, ProfileFlowEdge>()

  function accumulator(profile: string): Accumulator {
    const found = accumulators.get(profile)
    if (found) {
      return found
    }
    const created: Accumulator = {
      profile,
      pool: 0,
      poolByStatus: { ...EMPTY_STATUS },
      cleared: 0,
      entered: 0,
      blocked: 0,
      depths: [],
    }
    accumulators.set(profile, created)
    return created
  }

  for (const run of runs) {
    const depths = itemDepths(run.workItems)
    const byId = new Map(run.workItems.map((item) => [item.id, item]))
    const passed = new Set<string>()

    for (const item of run.workItems) {
      const node = accumulator(item.profile)
      node.depths.push(depths.get(item.id) ?? 0)

      // Observed transitions. An edge is only real once the upstream item has
      // finished: a dependency that has not fired yet is a plan, not a flow.
      for (const dependency of item.dependsOn) {
        const upstream = byId.get(dependency)
        if (!upstream || upstream.status !== "success") {
          continue
        }
        const key = `${upstream.profile} -> ${item.profile}`
        const edge = edgeCounts.get(key)
        if (edge) {
          edge.count += 1
        } else {
          edgeCounts.set(key, {
            from: upstream.profile,
            to: item.profile,
            count: 1,
          })
        }
      }

      if (item.status === "success") {
        passed.add(item.profile)
      }
    }

    // Where the run is standing, and in what state. The status is the run's own
    // — the value the list's status column shows — so a channel's segments and
    // the rows behind them can never disagree about what they are counting.
    const standing = currentItem(run)
    if (standing) {
      const node = accumulator(standing.profile)
      passed.delete(standing.profile)
      node.entered += 1
      if (run.status === "success") {
        node.cleared += 1
      } else {
        node.pool += 1
        node.poolByStatus[run.status] += 1
        if (BLOCKING.includes(run.status)) {
          node.blocked += 1
        }
      }
    }

    for (const profile of passed) {
      const node = accumulator(profile)
      node.cleared += 1
      node.entered += 1
    }
  }

  const nodes: ProfileFlowNode[] = [...accumulators.values()].map(
    ({ depths, ...node }) => ({ ...node, depth: median(depths) })
  )

  // Column order is a measurement, not a catalog: profiles sit where the
  // observed graphs put them. A tie shares a column — that is the board saying
  // these profiles run beside each other, rather than us asserting it.
  const bands = new Map<number, ProfileFlowNode[]>()
  for (const node of [...nodes].sort((a, b) =>
    a.profile.localeCompare(b.profile)
  )) {
    const band = bands.get(node.depth)
    if (band) {
      band.push(node)
      continue
    }
    bands.set(node.depth, [node])
  }

  const columns: ProfileFlowColumn[] = [...bands.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([depth, band]) => ({
      depth,
      parallel: band.length > 1,
      nodes: band,
    }))

  const rank = new Map<string, number>()
  columns.forEach((column, index) => {
    for (const node of column.nodes) {
      rank.set(node.profile, index)
    }
  })

  const edges: ProfileFlowEdge[] = [...edgeCounts.values()].sort(
    (a, b) =>
      b.count - a.count ||
      a.from.localeCompare(b.from) ||
      a.to.localeCompare(b.to)
  )

  // Second pass, because a gap cannot be named until the columns exist. Each
  // run marks the gaps its own observed transitions span and contributes one to
  // each — so a plan that invokes a profile eight times still crosses a gap
  // once, and the connector shares the channels' axis.
  const crossings = new Array<number>(Math.max(0, columns.length - 1)).fill(0)
  for (const run of runs) {
    const byId = new Map(run.workItems.map((entry) => [entry.id, entry]))
    const spanned = new Set<number>()

    for (const entry of run.workItems) {
      const to = rank.get(entry.profile)
      if (to === undefined) {
        continue
      }
      for (const dependency of entry.dependsOn) {
        const upstream = byId.get(dependency)
        if (!upstream || upstream.status !== "success") {
          continue
        }
        const from = rank.get(upstream.profile)
        if (from === undefined) {
          continue
        }
        for (let gap = from; gap < to; gap += 1) {
          spanned.add(gap)
        }
      }
    }

    for (const gap of spanned) {
      crossings[gap] += 1
    }
  }

  let pinch: ProfileFlowNode | null = null
  for (const node of nodes) {
    if (node.blocked === 0) {
      continue
    }
    if (!pinch || node.blocked > pinch.blocked) {
      pinch = node
      continue
    }
    if (node.blocked === pinch.blocked && node.pool > pinch.pool) {
      pinch = node
    }
  }

  return {
    columns,
    order: columns.flatMap((column) =>
      column.nodes.map((node) => node.profile)
    ),
    edges,
    crossings,
    scale: nodes.reduce((max, node) => Math.max(max, node.entered), 0),
    pinchProfile: pinch?.profile ?? null,
    // The header counts runs, not items: "3 waiting on a human" has to mean
    // three tickets a person owns, whatever their plans are made of.
    blockedTotal: runs.filter((run) => BLOCKING.includes(run.status)).length,
    runningTotal: runs.filter((run) => run.status === "running").length,
    total: runs.length,
  }
}

/**
 * Triage order: what "worst first" means here, as a rank. A status column
 * sorted alphabetically is noise — `escalated`, `failed`, `queued`, `running`
 * — so this is the order the duty screen means whenever it orders by status,
 * whether that is the default list order (`triageOrder`) or the user clicking
 * the status header. One rank, both consumers; a second copy would drift.
 */
export const TRIAGE_RANK: Record<RunStatus, number> = {
  escalated: 0,
  failed: 1,
  waiting: 2,
  running: 3,
  queued: 4,
  success: 5,
}

/**
 * Runs worst-first, so triage reads top-down. The board no longer decides what
 * the list contains — the profile filter does — so this only orders rows; it
 * never removes any.
 */
export function triageOrder(runs: RunSummary[]): RunSummary[] {
  return [...runs].sort((a, b) => {
    const byStatus = TRIAGE_RANK[a.status] - TRIAGE_RANK[b.status]
    return byStatus !== 0 ? byStatus : b.durationSec - a.durationSec
  })
}
