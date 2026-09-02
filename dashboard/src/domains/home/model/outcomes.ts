import type { SeedOutcomeDay } from "@/shared/api/mock/runs.seed"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"

/**
 * Run outcomes per day — the shift's history, which the runs list is not.
 *
 * The landing screen answers two questions: is a decision owed now, and how is
 * the week actually going. The first is `attention.ts`; this is the second. An
 * *outcome* is a run resting somewhere overnight — success, failed, escalated
 * — and explicitly not the mid-flight states, because a bar that stacked
 * "running" into a day's outcomes would be counting unfinished work as a
 * result of the day it happens to sit in.
 *
 * Today's column is bounded below by the live list for the same reason the
 * queue's depth is derived rather than authored: a week that finished fewer
 * runs today than the list is showing would be describing a different day than
 * the one on the screen. `assertOutcomesCoverRuns` is that contract, made
 * checkable.
 */

/** The three statuses a run can rest in overnight, worst on top of the stack. */
export type OutcomeStatus = Extract<
  RunStatus,
  "success" | "failed" | "escalated"
>

export const OUTCOME_STATUSES: readonly OutcomeStatus[] = [
  "success",
  "failed",
  "escalated",
]

export interface OutcomeCount {
  status: OutcomeStatus
  count: number
}

export interface OutcomeDay {
  /** The day's short label ("mon", "today"). */
  label: string
  /** The day's finished runs, in stack order: success at the base, worst on top. */
  outcomes: OutcomeCount[]
}

export function toOutcomeDays(seed: SeedOutcomeDay[]): OutcomeDay[] {
  return seed.map((day) => ({
    label: day.weekday,
    outcomes: day.byStatus
      .filter((entry): entry is { status: OutcomeStatus; count: number } =>
        OUTCOME_STATUSES.includes(entry.status as OutcomeStatus)
      )
      .map((entry) => ({ status: entry.status, count: entry.count })),
  }))
}

/** One day's finished runs, all statuses summed. */
export function outcomeDayTotal(day: OutcomeDay): number {
  return day.outcomes.reduce((total, entry) => total + entry.count, 0)
}

/** The window's total for one status — the figure's "23 failed this week". */
export function outcomeWindowTotal(
  days: OutcomeDay[],
  status: OutcomeStatus
): number {
  return days.reduce(
    (total, day) =>
      total + (day.outcomes.find((entry) => entry.status === status)?.count ?? 0),
    0
  )
}

/**
 * The seed's own contract: today's column cannot be smaller than the live
 * list it sits beside.
 *
 * Every run `runs` shows in a finished state happened on today's shift, so a
 * smaller column would be a history disagreeing with the present on the same
 * screen. Returns the statuses that broke the contract, empty when it holds.
 */
export function outcomesNotCovering(
  days: OutcomeDay[],
  runs: ReadonlyArray<Pick<RunSummary, "status">>
): OutcomeStatus[] {
  const today = days[days.length - 1]
  if (!today) {
    return [...OUTCOME_STATUSES]
  }

  return OUTCOME_STATUSES.filter((status) => {
    const listed = runs.filter((run) => run.status === status).length
    const finished = today.outcomes.find((entry) => entry.status === status)?.count ?? 0
    return finished < listed
  })
}
