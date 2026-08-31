import { describe, expect, it } from "vitest"

import { toRunSummary } from "@/domains/runs/api/mappers"
import { buildProfileFlow } from "@/domains/runs/model/profile-flow"
import { currentLabel, currentProfile } from "@/domains/runs/model/work-items"
import { uniqueProjects } from "@/domains/runs/model/filter-runs"
import { createRunColumns } from "@/domains/runs/ui/runs-columns"
import { PROJECTS_SEED, RUNS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import type { Session } from "@/shared/session"
import {
  applyDataFilters,
  dataFilterSpecs,
  type DataColumnFilter,
} from "@/shared/ui"

/**
 * The columns take the session as a value, not through a hook — which is what
 * lets this file build them with no React at all. The permission questions
 * themselves are `runs-columns.gate.test.tsx`; here it is the declarations.
 */
const session: Session = {
  user: SESSION_USER_SEED,
  projects: PROJECTS_SEED,
}

const runs = RUNS_SEED.map(toRunSummary)
const flow = buildProfileFlow(runs)
const projects = uniqueProjects(runs, PROJECTS_SEED)
const columns = createRunColumns({
  apps: [],
  projects,
  profiles: flow.order,
  approvingId: null,
  cancellingId: null,
  onApprove: () => {},
  onCancel: () => {},
  session,
})

function optionsOf(id: string): string[] {
  const spec = dataFilterSpecs(columns).find((entry) => entry.id === id)
  const filter = spec?.filter as
    Extract<DataColumnFilter<never>, { kind: "select" }> | undefined
  return (filter?.options ?? []).map((option) => option.value)
}

describe("the duty list's columns", () => {
  it("offers the profiles the board derived, in the board's order", () => {
    expect(optionsOf("profile")).toEqual(flow.order)
  })

  it("offers every project the shift is watching, filtering by id", () => {
    // The seeded swarm works in all three, so all three are offered — and the
    // option's *value* is the id the row carries while its label is the key
    // the operator reads.
    expect(optionsOf("project")).toEqual(["p_comuki", "p_plexor", "p_atlas"])

    for (const project of projects) {
      const rows = applyDataFilters(runs, { project: project.id }, columns)

      expect(rows.length).toBeGreaterThan(0)
      expect(rows.every((run) => run.projectId === project.id)).toBe(true)
    }
  })

  it("mixes the projects rather than sorting the list into blocks", () => {
    // The whole reason permission is a row-level question: the duty list is
    // one swarm, and the projects in it are interleaved. If the seed ever
    // grouped them, a single answer per screen would start looking correct.
    const ids = [...new Set(runs.map((run) => run.projectId))]
    const firstTwenty = new Set(runs.slice(0, 20).map((run) => run.projectId))

    expect(ids.length).toBe(3)
    expect(firstTwenty.size).toBeGreaterThan(1)
  })

  it("filters to exactly the runs the board node was counting", () => {
    for (const node of flow.columns.flatMap((column) => column.nodes)) {
      const rows = applyDataFilters(runs, { profile: node.profile }, columns)
      // A finished run is not standing anywhere, so the node counts it as
      // cleared while the list still shows it under the profile it ended on.
      const finished = rows.filter((run) => run.status === "success").length

      expect(rows.every((run) => currentProfile(run) === node.profile)).toBe(
        true
      )
      expect(rows.length - finished).toBe(node.pool)
    }
  })

  it("lets the text filter reach the brain's own step name", () => {
    const needle = currentLabel(runs[0]).slice(0, 12)
    const rows = applyDataFilters(runs, { title: needle }, columns)

    expect(rows.length).toBeGreaterThan(0)
    expect(
      rows.every((run) =>
        `${run.id} ${run.title} ${run.app} ${currentLabel(run)}`
          .toLowerCase()
          .includes(needle.toLowerCase())
      )
    ).toBe(true)
    expect(rows.some((run) => currentLabel(run).includes(needle))).toBe(true)
  })
})
