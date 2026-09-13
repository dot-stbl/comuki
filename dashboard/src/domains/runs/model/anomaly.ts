import type { AnomalyFlag, RunSummary } from "./types"

/**
 * Anomaly detection — the duty engineer's first scan of the list catches the
 * bad run before the cost page does.
 *
 * The rule lives in `anomalyFor(run, peerCosts)` and is the only place the
 * 3× median threshold is written down. A new rule (token spike, model mix)
 * would land here as a sibling helper and the mapper would union them.
 *
 * `peerCosts` is the project's own spend distribution — the median is
 * measured across the same project the run belongs to, not across the
 * whole swarm. A heavy `prometheus` run would never be an anomaly
 * because the *whole* project is heavy; the rule is per-project.
 */

/** The threshold. A run is anomalous when its cost is `× MULTIPLIER` the median. */
const MULTIPLIER = 3

/**
 * Median of a numeric array, lower of the two middle values for even-length
 * inputs — the same tie-breaking rule the duty board uses (`profile-flow.ts`).
 *
 * An empty array has no median; a single-value array uses that value.
 */
function median(values: number[]): number {
  if (values.length === 0) {
    return 0
  }
  const sorted = [...values].sort((left, right) => left - right)
  const middle = Math.floor(sorted.length / 2)
  if (sorted.length % 2 === 0 && middle > 0) {
    return (sorted[middle - 1] + sorted[middle]) / 2
  }
  return sorted[middle] ?? 0
}

/**
 * Group the runs by project and compute a per-project median, then read
 * the rule for each row. Doing it per-row would recompute the median
 * `runs.length` times; one pass is `O(runs)` end-to-end.
 */
export function flagAnomalies(runs: RunSummary[]): Map<string, AnomalyFlag> {
  const byProject = new Map<string, number[]>()
  for (const run of runs) {
    const bucket = byProject.get(run.projectId) ?? []
    bucket.push(run.cost)
    byProject.set(run.projectId, bucket)
  }

  const medians = new Map<string, number>()
  for (const [projectId, costs] of byProject) {
    medians.set(projectId, median(costs))
  }

  const flags = new Map<string, AnomalyFlag>()
  for (const run of runs) {
    const projectMedian = medians.get(run.projectId) ?? 0
    if (projectMedian <= 0) {
      continue
    }
    if (run.cost > projectMedian * MULTIPLIER) {
      const multiplier = run.cost / projectMedian
      flags.set(run.id, {
        multiplier: Math.round(multiplier * 10) / 10,
        medianCost: Math.round(projectMedian * 100) / 100,
        reason: "cost-spike",
      })
    }
  }
  return flags
}

/**
 * A one-shot helper for tests / places that want the flag for a single run
 * without recomputing the rest of the project's distribution.
 */
export function anomalyFor(
  run: RunSummary,
  peerCosts: number[]
): AnomalyFlag | null {
  const projectMedian = median(peerCosts)
  if (projectMedian <= 0) {
    return null
  }
  if (run.cost <= projectMedian * MULTIPLIER) {
    return null
  }
  return {
    multiplier: Math.round((run.cost / projectMedian) * 10) / 10,
    medianCost: Math.round(projectMedian * 100) / 100,
    reason: "cost-spike",
  }
}

/** The threshold, exported for the row's tooltip and the breakdown modal. */
export const ANOMALY_MULTIPLIER = MULTIPLIER
