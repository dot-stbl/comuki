import { RUNS_SEED, type SeedRun } from "./runs.seed"

/**
 * Mutable mock store.
 *
 * The seed is a constant; a query that maps it can never show the result of a
 * decision, because the refetch after a mutation restores the constant. This
 * holds the swarm's live state for the session so approving or cancelling a run
 * actually sticks — the same thing the real endpoint will do, minus the wire.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 */

function clone(run: SeedRun): SeedRun {
  return { ...run, items: run.items.map((entry) => ({ ...entry })) }
}

let runs: SeedRun[] = RUNS_SEED.map(clone)

export function listSeedRuns(): SeedRun[] {
  return runs
}

export function findSeedRun(runId: string): SeedRun | undefined {
  return runs.find((run) => run.id === runId)
}

/** Releasing a run back to the swarm: it stops waiting on a human. */
export function approveSeedRun(runId: string): void {
  runs = runs.map((run) => {
    if (run.id !== runId) {
      return run
    }
    return {
      ...run,
      status: "running",
      items: run.items.map((entry) =>
        entry.id === run.current ? { ...entry, status: "running" } : entry
      ),
    }
  })
}

/** Cancelling tears the container down: the run leaves the swarm entirely. */
export function cancelSeedRun(runId: string): void {
  runs = runs.filter((run) => run.id !== runId)
}

/** Back to the seeded shift — used by tests and stories. */
export function resetSeedRuns(): void {
  runs = RUNS_SEED.map(clone)
}
