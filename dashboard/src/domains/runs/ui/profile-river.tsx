import { Fragment, useEffect, useRef } from "react"
import type { CSSProperties, KeyboardEvent } from "react"

import type {
  ProfileFlow,
  ProfileFlowColumn,
  ProfileFlowNode,
} from "@/domains/runs/model/profile-flow"
import type { RunStatus } from "@/domains/runs/model/types"
import { useValueChanged } from "@/shared/hooks/use-value-changed"
import { cn } from "@/shared/lib/utils"

import styles from "./profile-river.module.css"

export interface ProfileRiverProps {
  flow: ProfileFlow
  /** The profile the table is filtered to, or null when it is showing them all. */
  selected: string | null
  onSelect: (profile: string) => void
  className?: string
}

/** Worst first: the pool reads top-down in the order a duty engineer triages. */
const SEGMENT_ORDER: RunStatus[] = [
  "escalated",
  "failed",
  "waiting",
  "running",
  "queued",
  "success",
]

function share(value: number, scale: number): number {
  if (scale <= 0 || value <= 0) {
    return 0
  }
  return Math.max(2, Math.round((value / scale) * 100))
}

function columnKey(column: ProfileFlowColumn): string {
  return column.nodes.map((node) => node.profile).join("+")
}

function entered(column: ProfileFlowColumn): number {
  return column.nodes.reduce((sum, node) => sum + node.entered, 0)
}

/**
 * A gap that far fewer runs got past than reached the column feeding it is
 * where the swarm stopped moving: the narrowing is the reading, so it earns the
 * hue. Both figures are observed — runs that arrived, runs whose own
 * transitions crossed — rather than an assumed pipeline.
 */
function narrowsAt(flow: ProfileFlow, index: number): boolean {
  const arrived = entered(flow.columns[index])
  return arrived > 0 && (flow.crossings[index] ?? 0) < arrived * 0.75
}

/**
 * The pool in the domain's own words — the six statuses, nothing invented —
 * worst-first, so the line under the channel and the channel itself say the
 * same thing in the same order.
 */
function composition(node: ProfileFlowNode): string[] {
  return SEGMENT_ORDER.filter((status) => node.poolByStatus[status] > 0).map(
    (status) => `${node.poolByStatus[status]} ${status}`
  )
}

/**
 * The band's composition in words, so the hue and hatch are never the only
 * channel. The label replaces the button's inner text for assistive tech, so
 * the marked profile has to be named here too — otherwise the one thing this
 * screen points at first is a sighted-only cue.
 */
function describe(node: ProfileFlowNode, marked: boolean): string {
  const parts = composition(node)
  const pool = parts.length > 0 ? parts.join(", ") : "no work"
  const mark = marked
    ? ` ${node.blocked} waiting on a human, more than any other profile.`
    : ""

  return `${node.profile}: ${pool}. ${node.cleared} cleared the profile.${mark}`
}

interface SegmentsProps {
  node: ProfileFlowNode
}

/** The pool drawn as one band: hue and weave together, worst-first. */
function Segments({ node }: SegmentsProps) {
  return (
    <>
      {SEGMENT_ORDER.map((status) => {
        const count = node.poolByStatus[status]
        if (count <= 0) {
          return null
        }
        return (
          <span
            key={status}
            className={cn(styles.seg, styles.status)}
            data-status={status}
            style={{ flexGrow: count }}
          />
        )
      })}
      {node.cleared > 0 ? (
        <span className={styles.cleared} style={{ flexGrow: node.cleared }} />
      ) : null}
    </>
  )
}

interface RiverNodeProps {
  node: ProfileFlowNode
  scale: number
  selected: boolean
  marked: boolean
  index: number
  onSelect: (profile: string) => void
}

function RiverNode({
  node,
  scale,
  selected,
  marked,
  index,
  onSelect,
}: RiverNodeProps) {
  // The big slot always measures the same thing — the work items sitting on
  // the profile — so every column compares on a two-second scan. Under it the
  // pool splits into the real statuses; the marked profile says how many of
  // those are waiting on a human on its own line rather than swapping the
  // quantity.
  const moved = useValueChanged(node.pool)

  return (
    <button
      type="button"
      data-test="river-node"
      data-profile={node.profile}
      aria-pressed={selected}
      aria-label={describe(node, marked)}
      className={cn(styles.node, marked && styles.markedNode)}
      onClick={() => onSelect(node.profile)}
    >
      <span className={styles.label}>{node.profile}</span>

      <span className={styles.track}>
        <span
          className={styles.band}
          style={
            {
              height: `${share(node.entered, scale)}%`,
              "--index": index,
            } as CSSProperties
          }
        >
          <Segments node={node} />
        </span>
      </span>

      <span className={styles.metrics}>
        <span className={cn(styles.pool, moved && styles.moved)}>
          {node.pool}
        </span>
        <span className={styles.mix}>{composition(node).join(" · ")}</span>
      </span>

      {/* Reserved on every node, filled on one: a mark that added a line would
          shorten its own channel and take that profile off the shared axis. */}
      <span className={styles.mark}>
        {marked ? `${node.blocked} waiting on a human` : null}
      </span>
    </button>
  )
}

const LEGEND: Array<[RunStatus, string]> = [
  ["running", "running"],
  ["waiting", "waiting on a human"],
  ["escalated", "escalated"],
  ["failed", "failed"],
]

/** Lives beside the river so the weave is defined once and read once. */
export function RiverLegend() {
  return (
    <ul className={styles.legend}>
      <li className={styles.key}>
        <span className={cn(styles.keySwatch, styles.keyCleared)} />
        cleared
      </li>
      {LEGEND.map(([status, label]) => (
        <li key={status} className={styles.key}>
          <span
            className={cn(styles.keySwatch, styles.status)}
            data-status={status}
          />
          {label}
        </li>
      ))}
    </ul>
  )
}

interface ConnectorProps {
  carried: number
  narrow: boolean
  scale: number
  index: number
}

function Connector({ carried, narrow, scale, index }: ConnectorProps) {
  return (
    <div className={styles.link} aria-hidden="true">
      <span className={styles.linkTrack}>
        <span
          className={cn(styles.linkBar, narrow && styles.linkNarrow)}
          style={
            {
              height: `${Math.max(2, share(carried, scale))}%`,
              "--index": index,
            } as CSSProperties
          }
        />
      </span>
    </div>
  )
}

export function ProfileRiver({
  flow,
  selected,
  onSelect,
  className,
}: ProfileRiverProps) {
  const ref = useRef<HTMLDivElement>(null)

  // On a narrow desk the river outruns its scroll container: bring the filtered
  // profile into the scrollport rather than letting it sit off the right edge.
  // `nearest` moves the scrolling ancestor only — never the page.
  useEffect(() => {
    if (!selected) {
      return
    }
    const node = ref.current?.querySelector<HTMLElement>(
      `[data-profile="${selected}"]`
    )
    if (!node) {
      return
    }
    const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches
    node.scrollIntoView({
      block: "nearest",
      inline: "nearest",
      behavior: still ? "auto" : "smooth",
    })
  }, [selected])

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") {
      return
    }
    const found = ref.current?.querySelectorAll<HTMLButtonElement>(
      '[data-test="river-node"]'
    )
    if (!found || found.length === 0) {
      return
    }
    const nodes = [...found]
    const current = nodes.indexOf(document.activeElement as HTMLButtonElement)
    if (current === -1) {
      return
    }
    event.preventDefault()
    const step = event.key === "ArrowRight" ? 1 : -1
    const next = nodes[(current + step + nodes.length) % nodes.length]
    next.focus()
    if (next.dataset.profile) {
      onSelect(next.dataset.profile)
    }
  }

  return (
    <div
      ref={ref}
      className={cn(styles.river, className)}
      data-test="profile-river"
      role="group"
      aria-label="Profile flow. Arrow keys move between profiles."
      onKeyDown={onKeyDown}
    >
      {flow.columns.map((column, columnIndex) => (
        <Fragment key={columnKey(column)}>
          <div
            className={styles.column}
            data-parallel={column.parallel ? "" : undefined}
          >
            {column.nodes.map((node) => (
              <RiverNode
                key={node.profile}
                node={node}
                scale={flow.scale}
                index={columnIndex}
                selected={selected === node.profile}
                marked={flow.pinchProfile === node.profile}
                onSelect={onSelect}
              />
            ))}
          </div>

          {columnIndex < flow.columns.length - 1 ? (
            <Connector
              carried={flow.crossings[columnIndex] ?? 0}
              narrow={narrowsAt(flow, columnIndex)}
              scale={flow.scale}
              index={columnIndex}
            />
          ) : null}
        </Fragment>
      ))}
    </div>
  )
}

export interface ProfileStripProps {
  flow: ProfileFlow
  onExpand: () => void
  className?: string
}

/**
 * The board collapsed: the same flow squeezed to roughly one row, with no
 * numbers and no labels. Only the shape survives — which profile is fat, where
 * the river narrows — which is what a glance at a parked board is for. The
 * strip is itself the control that brings the board back, so the shape is
 * never a dead end for a pointer or for a keyboard.
 */
export function ProfileStrip({ flow, onExpand, className }: ProfileStripProps) {
  return (
    <button
      type="button"
      data-test="profile-strip"
      className={cn(styles.strip, className)}
      aria-label="Expand the flow board"
      aria-expanded={false}
      onClick={onExpand}
    >
      {flow.columns.map((column, columnIndex) => (
        <Fragment key={columnKey(column)}>
          <span className={styles.stripColumn}>
            {column.nodes.map((node) => (
              <span key={node.profile} className={styles.stripLane}>
                <span
                  className={styles.stripBand}
                  style={{ height: `${share(node.entered, flow.scale)}%` }}
                >
                  <Segments node={node} />
                </span>
              </span>
            ))}
          </span>

          {columnIndex < flow.columns.length - 1 ? (
            <span className={styles.stripLink}>
              <span
                className={cn(
                  styles.stripBar,
                  narrowsAt(flow, columnIndex) && styles.linkNarrow
                )}
                style={{
                  height: `${Math.max(
                    2,
                    share(flow.crossings[columnIndex] ?? 0, flow.scale)
                  )}%`,
                }}
              />
            </span>
          ) : null}
        </Fragment>
      ))}
    </button>
  )
}
