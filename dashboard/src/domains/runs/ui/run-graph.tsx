import { Fragment, useCallback, useMemo, useRef, useState } from "react"
import type { KeyboardEvent } from "react"

import type { WorkItem } from "@/domains/runs/model/types"
import {
  isLongEdge,
  planGraph,
  type BlockedReason,
  type ItemDependency,
} from "@/domains/runs/model/work-items"
import { cn } from "@/shared/lib/utils"

import styles from "./run-graph.module.css"

/**
 * The run graph — the brain's plan, drawn as layers.
 *
 * Columns are depth bands, left to right; a branch is neighbouring rows inside
 * one column; the connector between two columns is the same ribbon the duty
 * board runs between profiles. The form was chosen over a node-link canvas
 * because a duty console does not get zoom and pan, and because this is the
 * grammar the board already speaks — a person learns it once.
 *
 * Presentational: work items and a selection come in, a selection change goes
 * out. Everything it knows about the graph it reads through the domain model.
 */

export interface RunGraphProps {
  items: WorkItem[]
  /** Id of the item the run is standing on. */
  current?: string
  /** Id of the item the inspector is showing. */
  selected?: string
  /**
   * Omit to draw the plan as static content — an approval's plan preview has
   * nothing to select, and a control that does nothing is worse than no
   * control. With it, every node is a real button.
   */
  onSelect?: (itemId: string) => void
  /**
   * How the graph is sized, and it is a contract rather than a preference.
   * `fill` takes the parent's height — the parent must have a definite one —
   * and lets tall columns scroll inside themselves. `content` lets the tallest
   * column set the height, capped, for a graph inside a card that flows.
   */
  fit?: "fill" | "content"
  /** Names the region for assistive tech. */
  label?: string
  className?: string
}

/** What a node has to say about itself that the layout cannot. */
interface NodeMarks {
  blocked: BlockedReason | undefined
  long: ItemDependency[]
  current: boolean
}

/**
 * The marks in reading order. The long-edge mark comes first on purpose: a
 * blocked node has a dashed border and a current node has its own slot, but a
 * dependency that skips a column has nothing else carrying it, so it is the
 * one that has to survive the line running out of room.
 */
function noteOf(marks: NodeMarks): string {
  const parts: string[] = []
  if (marks.long.length > 0) {
    parts.push(`depends on ${marks.long.length} earlier`)
  }
  if (marks.blocked) {
    parts.push("blocked")
  }
  return parts.join(" · ")
}

/**
 * The node in words. The accessible name replaces the button's inner text, so
 * everything the node shows has to be said here — and the two things it can
 * only imply, the distant dependencies and the reason it is stuck, are spelled
 * out rather than left to a hover.
 */
function describe(item: WorkItem, marks: NodeMarks): string {
  const parts = [`${item.label}.`, `${item.profile}, ${item.status}.`]

  if (marks.current) {
    parts.push("the run is standing here.")
  }
  if (marks.blocked === "failed") {
    parts.push("blocked by a failure upstream.")
  }
  if (marks.blocked === "escalated") {
    parts.push("blocked by an escalation upstream.")
  }
  if (marks.long.length > 0) {
    const named = marks.long
      .map((entry) => `${entry.item.label} (${entry.item.profile})`)
      .join(", ")
    const plural = marks.long.length === 1 ? "" : "s"
    parts.push(
      `depends on ${marks.long.length} item${plural} more than one column back: ${named}.`
    )
  }

  return parts.join(" ")
}

interface GraphNodeProps {
  item: WorkItem
  column: number
  row: number
  marks: NodeMarks
  selected: boolean
  trace: "from" | "to" | undefined
  traceLong: boolean
  /** Roving tab stop — one node in the whole graph is reachable by Tab. */
  tabbable: boolean
  onSelect?: (itemId: string) => void
  onTrace: (itemId: string | null) => void
}

function NodeBody({ item, marks }: { item: WorkItem; marks: NodeMarks }) {
  return (
    <>
      <span
        className={cn(styles.edge, styles.status)}
        data-status={item.status}
        aria-hidden="true"
      />
      {/* Two names, and they are not the same kind of thing. The label is the
          brain's prose for this ticket, and this screen is the only place it
          is written out in full, so it gets the weight. The profile under it
          is the catalog key declared in git — the identity, and the thing a
          person can go and edit. */}
      <span className={styles.label}>{item.label}</span>
      <span className={styles.meta}>
        <span className={styles.profile}>{item.profile}</span>
        <span className={styles.state}>{item.status}</span>
        {marks.current ? <span className={styles.here}>here</span> : null}
      </span>
      <span className={styles.note}>{noteOf(marks)}</span>
    </>
  )
}

function GraphNode({
  item,
  column,
  row,
  marks,
  selected,
  trace,
  traceLong,
  tabbable,
  onSelect,
  onTrace,
}: GraphNodeProps) {
  const note = noteOf(marks)
  // Two lines of label is enough for most of the plans the brain writes and
  // not for all of them; the pointer always gets the whole string.
  const title = note ? `${item.label} — ${note}` : item.label

  if (!onSelect) {
    // Static: the node reads out its own text, which already carries the
    // label, the profile, the state and the marks. No control, so there is no
    // accessible name to override and nothing to focus.
    return (
      <div
        className={styles.node}
        role="listitem"
        data-test="work-item-node"
        data-item={item.id}
        data-status={item.status}
        data-blocked={marks.blocked}
        title={title}
      >
        <NodeBody item={item} marks={marks} />
      </div>
    )
  }

  return (
    <button
      type="button"
      className={cn(styles.node, styles.interactive)}
      data-test="work-item-node"
      data-item={item.id}
      data-column={column}
      data-row={row}
      data-status={item.status}
      data-blocked={marks.blocked}
      data-trace={trace}
      data-long={traceLong ? "" : undefined}
      aria-pressed={selected}
      aria-label={describe(item, marks)}
      tabIndex={tabbable ? 0 : -1}
      title={title}
      onClick={() => onSelect(item.id)}
      onPointerEnter={() => onTrace(item.id)}
      onPointerLeave={() => onTrace(null)}
      onFocus={() => onTrace(item.id)}
      onBlur={() => onTrace(null)}
    >
      <NodeBody item={item} marks={marks} />
    </button>
  )
}

export function RunGraph({
  items,
  current,
  selected,
  onSelect,
  fit = "fill",
  label = "Run graph. Arrow keys move between work items.",
  className,
}: RunGraphProps) {
  const ref = useRef<HTMLDivElement>(null)
  const [traced, setTraced] = useState<string | null>(null)

  const graph = useMemo(() => planGraph(items), [items])

  // The roving tab stop. Forty-two tab stops is not navigation, it is a
  // punishment; selection is what the inspector is showing, so it is also the
  // sensible place for Tab to land. A graph with nothing selected opens on its
  // first item rather than on nothing.
  const anchor =
    items.find((item) => item.id === selected)?.id ?? items[0]?.id ?? ""

  /** The items the traced node waits on, and whether each one is a long edge. */
  const tracedDeps = useMemo(() => {
    const found = new Map<string, boolean>()
    if (!traced) {
      return found
    }
    for (const dependency of graph.dependencies.get(traced) ?? []) {
      found.set(dependency.item.id, isLongEdge(dependency))
    }
    return found
  }, [graph, traced])

  const onTrace = useCallback((itemId: string | null) => {
    setTraced(itemId)
  }, [])

  /**
   * Arrow keys walk the graph the way it is drawn: left and right move a
   * column, up and down move a row inside one. Selection follows focus, which
   * is what makes the inspector below usable from the keyboard alone.
   */
  const onKeyDown = useCallback(
    (event: KeyboardEvent<HTMLDivElement>) => {
      const horizontal = event.key === "ArrowLeft" || event.key === "ArrowRight"
      const vertical = event.key === "ArrowUp" || event.key === "ArrowDown"
      if (!horizontal && !vertical) {
        return
      }
      const active = document.activeElement
      if (!(active instanceof HTMLElement) || !active.dataset.item) {
        return
      }

      const columnIndex = Number(active.dataset.column)
      const rowIndex = Number(active.dataset.row)
      const forward = event.key === "ArrowRight" || event.key === "ArrowDown"
      const step = forward ? 1 : -1

      let target: WorkItem | undefined
      if (horizontal) {
        const next = graph.columns[columnIndex + step]
        // Landing on the same row keeps a lane readable as a lane; a shorter
        // column takes the nearest row it has rather than refusing to move.
        target = next?.items[Math.min(rowIndex, next.items.length - 1)]
      } else {
        target = graph.columns[columnIndex]?.items[rowIndex + step]
      }
      if (!target) {
        return
      }

      event.preventDefault()
      onSelect?.(target.id)
      ref.current
        ?.querySelector<HTMLElement>(`[data-item="${target.id}"]`)
        ?.focus()
    },
    [graph, onSelect]
  )

  if (items.length === 0) {
    return <p className={styles.empty}>No work items in this plan.</p>
  }

  const last = graph.columns.length - 1

  return (
    <div
      ref={ref}
      className={cn(styles.graph, styles[fit], className)}
      data-test="run-graph"
      role="group"
      aria-label={label}
      onKeyDown={onSelect ? onKeyDown : undefined}
    >
      {graph.columns.map((column, columnIndex) => (
        <Fragment key={`depth-${column.depth}`}>
          <div
            className={styles.column}
            data-test="run-graph-column"
            data-parallel={column.parallel ? "" : undefined}
          >
            {/* Reserved on every column, filled where the plan branches. */}
            <span className={styles.head}>
              {column.parallel ? `${column.items.length} parallel` : null}
            </span>

            <div
              className={styles.items}
              role={onSelect ? undefined : "list"}
              data-test="run-graph-items"
            >
              {column.items.map((item, rowIndex) => (
                <GraphNode
                  key={item.id}
                  item={item}
                  column={columnIndex}
                  row={rowIndex}
                  marks={{
                    blocked: graph.blocked.get(item.id),
                    long: (graph.dependencies.get(item.id) ?? []).filter(
                      isLongEdge
                    ),
                    current: item.id === current,
                  }}
                  selected={selected === item.id}
                  trace={
                    traced === item.id
                      ? "from"
                      : tracedDeps.has(item.id)
                        ? "to"
                        : undefined
                  }
                  traceLong={tracedDeps.get(item.id) === true}
                  tabbable={item.id === anchor}
                  onSelect={onSelect}
                  onTrace={onTrace}
                />
              ))}
            </div>
          </div>

          {columnIndex < last ? (
            <span className={styles.link} aria-hidden="true" />
          ) : null}
        </Fragment>
      ))}
    </div>
  )
}
