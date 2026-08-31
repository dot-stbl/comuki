import { triageOrder } from "@/domains/runs/model/profile-flow"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"

/**
 * The attention reading — the whole of what the landing screen has to know.
 *
 * The screen answers one question: *is a decision owed, and where*. Everything
 * here exists to produce that one number and the rows behind it, and nothing
 * here invents a second definition of anything the product already has:
 *
 * - **which runs count** is the same three statuses `buildProfileFlow` folds
 *   into `blockedTotal`. The duty screen's header already says "N waiting on a
 *   human" from that set, and a landing screen that disagreed with it by one
 *   would be worse than no landing screen at all.
 * - **what order they read in** is `triageOrder` — the product's own "worst
 *   first", ranked by `TRIAGE_RANK` and broken by time in step. Not re-derived
 *   here; imported, so the two screens can never drift.
 *
 * What *is* decided here is the part the flow board has no opinion about: what
 * a person can do about each one, and in which words.
 */

/** The three statuses whose next move belongs to a person, worst-first. */
export type AttentionStatus = "escalated" | "failed" | "waiting"

/** An act a row offers inline, beside the observation that implies it. */
export type AttentionAct = "approve" | "stop" | "open"

interface AttentionKind {
  /** Why these runs are here, in the product's own six words. */
  reason: string
  /** The acts, in the order they sit on the row. `open` is always last. */
  acts: readonly AttentionAct[]
}

/**
 * The three kinds, and the one judgement this module makes.
 *
 * `escalated` and `waiting` carry the two decisions the duty list already
 * offers on exactly those two statuses — approve releases the run back to the
 * swarm, stop tears its container down. `failed` carries neither, and that is
 * deliberate rather than an omission: a failed gate has no one-click answer,
 * the run is holding no worker slot, and the honest next move is to open it and
 * read what the gate said. Offering a stop there would invent a semantic the
 * duty list does not have, and offering an approve would pretend a failure can
 * be waved through from a summary row.
 */
const KINDS: Record<AttentionStatus, AttentionKind> = {
  escalated: {
    reason: "raised past the swarm to a person",
    acts: ["approve", "stop", "open"],
  },
  failed: {
    reason: "stopped at a verification gate",
    acts: ["open"],
  },
  waiting: {
    reason: "waiting on a human",
    acts: ["approve", "stop", "open"],
  },
}

function isAttentionStatus(status: RunStatus): status is AttentionStatus {
  return status in KINDS
}

/** Does this run's next move belong to a person? */
export function needsHuman(run: RunSummary): boolean {
  return isAttentionStatus(run.status)
}

export interface AttentionItem {
  run: RunSummary
  status: AttentionStatus
  acts: readonly AttentionAct[]
}

export interface AttentionGroup {
  status: AttentionStatus
  reason: string
  acts: readonly AttentionAct[]
  items: AttentionItem[]
}

export interface AttentionReading {
  /** Runs owed a decision, worst-first. Its length is the screen's number. */
  items: AttentionItem[]
  /**
   * The same total, split into the statuses it is made of, in the order the
   * items themselves arrived — so the split can never claim an order the list
   * below it does not have.
   */
  mix: Array<{ status: AttentionStatus; count: number }>
  /** The worst status present, or `null` when nothing is owed. */
  worst: AttentionStatus | null
  /** Active runs, longest in step first: the ones nearest to becoming a row above. */
  running: RunSummary[]
  queued: number
  total: number
}

const EMPTY: AttentionReading = {
  items: [],
  mix: [],
  worst: null,
  running: [],
  queued: 0,
  total: 0,
}

export function readAttention(runs: RunSummary[]): AttentionReading {
  if (runs.length === 0) {
    return EMPTY
  }

  const items: AttentionItem[] = triageOrder(runs.filter(needsHuman)).flatMap(
    (run) =>
      isAttentionStatus(run.status)
        ? [{ run, status: run.status, acts: KINDS[run.status].acts }]
        : []
  )

  const counts = new Map<AttentionStatus, number>()
  for (const item of items) {
    counts.set(item.status, (counts.get(item.status) ?? 0) + 1)
  }

  return {
    items,
    mix: [...counts.entries()].map(([status, count]) => ({ status, count })),
    worst: items[0]?.status ?? null,
    // Longest in step first. A run that has been on one step far longer than
    // its neighbours is the one most likely to be a row in the block above by
    // the time the engineer looks again — which is the only ordering that
    // earns a place on a screen about what needs attention.
    running: runs
      .filter((run) => run.status === "running")
      .sort((a, b) => b.durationSec - a.durationSec),
    queued: runs.filter((run) => run.status === "queued").length,
    total: runs.length,
  }
}

/**
 * The list, in buckets.
 *
 * Grouping is what lets the rows drop their status column: the heading says it
 * once, in the product's own sentence, instead of a badge repeating it on every
 * row. Order is taken from the items rather than from a constant, so the groups
 * read worst-first because `triageOrder` said so.
 */
export function groupAttention(items: AttentionItem[]): AttentionGroup[] {
  const groups: AttentionGroup[] = []
  const byStatus = new Map<AttentionStatus, AttentionGroup>()

  for (const item of items) {
    const found = byStatus.get(item.status)
    if (found) {
      found.items.push(item)
      continue
    }
    const created: AttentionGroup = {
      status: item.status,
      reason: KINDS[item.status].reason,
      acts: item.acts,
      items: [item],
    }
    byStatus.set(item.status, created)
    groups.push(created)
  }

  return groups
}
