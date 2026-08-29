import type { RunSummary, RunsFilter } from "@/domains/runs/model/types"

export function filterRuns(
  runs: RunSummary[],
  filter: RunsFilter
): RunSummary[] {
  const query = filter.query.trim().toLowerCase()

  return runs.filter((run) => {
    if (filter.app !== "all" && run.app !== filter.app) {
      return false
    }
    if (filter.status !== "all" && run.status !== filter.status) {
      return false
    }
    if (query.length === 0) {
      return true
    }
    const haystack = `${run.id} ${run.title} ${run.app}`.toLowerCase()
    return haystack.includes(query)
  })
}

export function uniqueApps(runs: RunSummary[]): string[] {
  return [...new Set(runs.map((run) => run.app))].sort()
}

export function countActive(runs: RunSummary[]): number {
  return runs.filter(
    (run) => run.status === "running" || run.status === "escalated"
  ).length
}
