import type { SeedProject } from "@/shared/api/mock/projects.seed"

import type { ProjectRow } from "./types"

/**
 * What a project is doing, derived from the two lists that already know.
 *
 * A project that exists is running work and spending money, and a registry row
 * that says only "this project exists" is not worth the trip to the screen. So
 * the row is joined here from the run list and the cost report rather than
 * carrying its own counters — there is no third place for the numbers to be
 * wrong in.
 *
 * The input types are deliberately loose. This module reads two seeds it does
 * not own and that are being written by other hands; a field that is not there
 * yet has to degrade to a dash rather than take the screen down, so every
 * field it reads is optional and every missing one drops the fact rather than
 * the row.
 */

/** The part of a run this module reads. Everything optional, on purpose. */
export interface RunFact {
  projectId?: string
  app?: string
  status?: string
}

/** The part of a cost report row this module reads. */
export interface CostFact {
  app?: string
  spend?: number
}

/** The four statuses that mean the swarm is still standing on the run. */
const ACTIVE_STATUS = new Set(["running", "waiting", "queued", "escalated"])

/**
 * Which project an app belongs to, learned from the runs themselves.
 *
 * The mapping exists as a constant in the run seed, but reading it from the
 * rows means this module only ever depends on the one field it was told to
 * read defensively. An app nobody has run is simply not in the map, and its
 * spend goes unattributed rather than to the wrong project.
 */
function appOwners(runs: readonly RunFact[]): Map<string, string> {
  const owners = new Map<string, string>()
  for (const run of runs) {
    if (run.app && run.projectId && !owners.has(run.app)) {
      owners.set(run.app, run.projectId)
    }
  }
  return owners
}

export function buildProjectRows(
  projects: readonly SeedProject[],
  runs: readonly RunFact[],
  costs: readonly CostFact[]
): ProjectRow[] {
  const owners = appOwners(runs)

  const total = new Map<string, number>()
  const active = new Map<string, number>()
  for (const run of runs) {
    const id = run.projectId
    if (!id) {
      continue
    }
    total.set(id, (total.get(id) ?? 0) + 1)
    if (run.status && ACTIVE_STATUS.has(run.status)) {
      active.set(id, (active.get(id) ?? 0) + 1)
    }
  }

  const spend = new Map<string, number>()
  for (const cost of costs) {
    const id = cost.app ? owners.get(cost.app) : undefined
    if (!id || typeof cost.spend !== "number") {
      continue
    }
    spend.set(id, (spend.get(id) ?? 0) + cost.spend)
  }

  return projects.map((project) => ({
    id: project.id,
    slug: project.slug,
    name: project.name,
    gitProfileRepo: project.gitProfileRepo,
    createdAt: project.createdAt,
    activeRuns: active.get(project.id) ?? 0,
    totalRuns: total.get(project.id) ?? 0,
    // Absent, not zero: a project the cost report has never heard of has not
    // spent nothing, it has not been measured.
    spendToday: spend.has(project.id) ? (spend.get(project.id) as number) : null,
  }))
}
