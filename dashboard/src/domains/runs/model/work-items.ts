import type { RunSummary, WorkItem } from "@/domains/runs/model/types"

/**
 * Reading a plan graph.
 *
 * A plan is an arbitrary DAG of profile invocations, so nothing here may assume
 * a fixed order, a fixed length or a fixed set of names. Two things are needed
 * from that graph often enough to be written once: how deep an item sits, and
 * what order a human should read the items in.
 *
 * Depth is the **longest** path from a root, not the shortest: an item may not
 * be drawn before anything it waits on, and the longest path is the only depth
 * that guarantees that for every dependency at once.
 */

export interface WorkItemColumn {
  /** Depth of every item in this column. */
  depth: number
  /** More than one item — the plan branches here. */
  parallel: boolean
  items: WorkItem[]
}

/**
 * Depth per item id. Dependencies pointing outside the run are ignored, and a
 * cycle (which a valid plan never has, and a mock or a bad payload might) is
 * cut rather than allowed to recurse: the graph still renders, it just renders
 * the cycle flat.
 */
export function itemDepths(items: WorkItem[]): Map<string, number> {
  const byId = new Map(items.map((item) => [item.id, item]))
  const depths = new Map<string, number>()
  const visiting = new Set<string>()

  function depthOf(id: string): number {
    const known = depths.get(id)
    if (known !== undefined) {
      return known
    }
    const item = byId.get(id)
    if (!item || visiting.has(id)) {
      return 0
    }
    visiting.add(id)
    let depth = 0
    for (const dependency of item.dependsOn) {
      if (byId.has(dependency)) {
        depth = Math.max(depth, depthOf(dependency) + 1)
      }
    }
    visiting.delete(id)
    depths.set(id, depth)
    return depth
  }

  for (const item of items) {
    depthOf(item.id)
  }
  return depths
}

/**
 * The items in dependency order — every item after everything it waits on.
 * Ties inside a depth keep the order the plan arrived in, so a run reads the
 * same way twice.
 */
export function orderedItems(items: WorkItem[]): WorkItem[] {
  const depths = itemDepths(items)
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => {
      const byDepth =
        (depths.get(a.item.id) ?? 0) - (depths.get(b.item.id) ?? 0)
      return byDepth !== 0 ? byDepth : a.index - b.index
    })
    .map((entry) => entry.item)
}

/** The graph sliced into depth bands: one column per depth, in order. */
export function itemColumns(items: WorkItem[]): WorkItemColumn[] {
  const depths = itemDepths(items)
  const bands = new Map<number, WorkItem[]>()

  for (const item of orderedItems(items)) {
    const depth = depths.get(item.id) ?? 0
    const band = bands.get(depth)
    if (band) {
      band.push(item)
      continue
    }
    bands.set(depth, [item])
  }

  return [...bands.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([depth, band]) => ({
      depth,
      parallel: band.length > 1,
      items: band,
    }))
}

/**
 * The item a run is standing on: what `current` points at, or the first item
 * the run has, so a row and a detail page always have something to show.
 */
export function currentItem(run: {
  current: string
  workItems: WorkItem[]
}): WorkItem | undefined {
  return (
    run.workItems.find((item) => item.id === run.current) ?? run.workItems[0]
  )
}

/** The profile a run is occupying right now. The row's identity column. */
export function currentProfile(run: RunSummary): string {
  return currentItem(run)?.profile ?? ""
}

/** The brain's name for what the run is doing right now. Prose, not a key. */
export function currentLabel(run: RunSummary): string {
  return currentItem(run)?.label ?? ""
}

/* ---------------------------------------------------------------------------
 * Reading the graph as a drawn thing.
 *
 * `itemColumns` says where an item sits. Two more things have to be read off
 * the same graph before it can be *drawn* honestly, and both are exactly the
 * facts the layered form is worst at showing:
 *
 * 1. A dependency that skips a column. The layered form's whole promise is
 *    "a connection joins the band beside you", and an item waiting on a
 *    distant ancestor breaks it. The span is computed here so the drawing can
 *    mark it rather than quietly lose it.
 * 2. An item that is queued behind something that stopped. Its own status says
 *    `queued`, which is a lie about what a person can expect from it: nothing
 *    will happen until a human looks at the failure upstream.
 * ------------------------------------------------------------------------- */

/** One dependency of one item, with the distance a reader has to travel. */
export interface ItemDependency {
  item: WorkItem
  /**
   * Columns crossed. 1 is the neighbouring band — the layout draws that for
   * free. 2 or more is a long edge: the item waits on something the column
   * beside it does not contain, and no amount of looking left will find it.
   */
  span: number
}

/** The smallest span that the layered form cannot draw by adjacency alone. */
export const LONG_EDGE_SPAN = 2

/** A dependency the drawing has to account for explicitly. */
export function isLongEdge(dependency: ItemDependency): boolean {
  return dependency.span >= LONG_EDGE_SPAN
}

/**
 * Why a queued item is not merely waiting its turn. Both mean the same thing
 * to a duty engineer — a person has to look at something upstream first — and
 * they are kept apart because the sentence a screen reader hears differs.
 */
export type BlockedReason = "failed" | "escalated"

export interface PlanGraph {
  /** The depth bands, in order. */
  columns: WorkItemColumn[]
  /** Direct dependencies per item id, in the order the plan declared them. */
  dependencies: Map<string, ItemDependency[]>
  /** Queued items with a stopped ancestor, and what stopped up there. */
  blocked: Map<string, BlockedReason>
}

/**
 * What stops a queued item, if anything.
 *
 * Propagation is deliberately narrow. A `failed` or `escalated` item stops
 * everything queued behind it. A `success` or `running` item stops nothing —
 * it has moved or is moving, whatever happened before it. A `waiting` item is
 * a human gate in its normal state, not a fault, so it does not paint its
 * descendants as blocked: "waiting on a human mid-graph" is its own reading and
 * gets its own treatment. Only a queued item passes a stall further down.
 */
function stalls(items: WorkItem[]): Map<string, BlockedReason> {
  const byId = new Map(items.map((item) => [item.id, item]))
  const known = new Map<string, BlockedReason | null>()
  const visiting = new Set<string>()

  function stallOf(id: string): BlockedReason | null {
    const seen = known.get(id)
    if (seen !== undefined) {
      return seen
    }
    const item = byId.get(id)
    // A cycle is cut the same way `itemDepths` cuts one: the graph still
    // renders, it just stops claiming to know what is behind the cycle.
    if (!item || visiting.has(id)) {
      return null
    }
    if (item.status === "failed" || item.status === "escalated") {
      known.set(id, item.status)
      return item.status
    }
    if (item.status !== "queued") {
      known.set(id, null)
      return null
    }
    visiting.add(id)
    let reason: BlockedReason | null = null
    for (const dependency of item.dependsOn) {
      reason = stallOf(dependency)
      if (reason) {
        break
      }
    }
    visiting.delete(id)
    known.set(id, reason)
    return reason
  }

  const blocked = new Map<string, BlockedReason>()
  for (const item of items) {
    if (item.status !== "queued") {
      continue
    }
    // The item's own stall is inherited from its dependencies by construction;
    // reading it directly would report a queued item as blocking itself.
    const reason = stallOf(item.id)
    if (reason) {
      blocked.set(item.id, reason)
    }
  }
  return blocked
}

/** Everything a drawing of this plan needs, read off the graph once. */
export function planGraph(items: WorkItem[]): PlanGraph {
  const depths = itemDepths(items)
  const byId = new Map(items.map((item) => [item.id, item]))
  const dependencies = new Map<string, ItemDependency[]>()

  for (const item of items) {
    const depth = depths.get(item.id) ?? 0
    const resolved: ItemDependency[] = []
    for (const id of item.dependsOn) {
      const dependency = byId.get(id)
      if (!dependency) {
        // A dependency pointing outside the run is ignored here for the same
        // reason `itemDepths` ignores it: it cannot be drawn, and inventing a
        // node for it would be inventing a plan.
        continue
      }
      resolved.push({ item: dependency, span: depth - (depths.get(id) ?? 0) })
    }
    // Nearest band first, so "what did this wait on" reads outward from the
    // column beside it and the long edges arrive last, where they stand out.
    resolved.sort((a, b) => a.span - b.span)
    dependencies.set(item.id, resolved)
  }

  return {
    columns: itemColumns(items),
    dependencies,
    blocked: stalls(items),
  }
}

/** One item's direct dependencies, nearest band first. */
export function dependenciesOf(
  items: WorkItem[],
  itemId: string
): ItemDependency[] {
  return planGraph(items).dependencies.get(itemId) ?? []
}
