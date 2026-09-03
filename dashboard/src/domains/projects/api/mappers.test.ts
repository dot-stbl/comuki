import { describe, expect, it } from "vitest"

import {
  mapCostsPageToCostSummary,
  mapCreateProjectInputToCreateRequest,
  mapProjectSettingsToUpdateRequest,
  mapProjectSettingsViewToSettings,
  mapProjectViewToDetail,
  mapProjectsPageToSummaries,
  toProjectRow,
} from "@/domains/projects/api/mappers"

/**
 * Wire → domain mappers for the projects domain.
 *
 * The kubb-generated response types are `any` because the host's OpenAPI
 * document does not include response schemas for the projects endpoints.
 * The mapper interfaces (`ProjectView`, `ProjectSettingsView`,
 * `ProjectCostsView`) mirror the C# record types property-for-property,
 * so the assertions below exercise the real wire shape.
 */

function projectViewFixture(
  overrides: Partial<ProjectViewStub> = {}
): ProjectViewStub {
  return {
    id: "00000000-0000-0000-0000-0000000000aa",
    name: "Comuki platform",
    slug: "comuki",
    description: null,
    profilesGitUrl: "git@github.com:comuki/worker-profiles.git",
    profilesGitRef: null,
    archived: false,
    archivedAt: null,
    createdAt: "2026-03-04T00:00:00.000+00:00",
    updatedAt: "2026-09-04T00:00:00.000+00:00",
    ...overrides,
  }
}

interface ProjectViewStub {
  id: string
  name: string
  slug: string
  description: string | null
  profilesGitUrl: string | null
  profilesGitRef: string | null
  archived: boolean
  archivedAt: string | null
  createdAt: string
  updatedAt: string
}

describe("mapProjectViewToDetail", () => {
  it("carries id, slug, name and createdAt through verbatim", () => {
    const row = mapProjectViewToDetail(projectViewFixture({ slug: "vega" }))

    expect(row.id).toBe("00000000-0000-0000-0000-0000000000aa")
    expect(row.slug).toBe("vega")
    expect(row.name).toBe("Comuki platform")
    expect(row.createdAt).toBe("2026-03-04T00:00:00.000+00:00")
  })

  it("collapses the wire's URL/ref into the domain's single gitProfileRepo handle", () => {
    const row = mapProjectViewToDetail(
      projectViewFixture({
        profilesGitUrl: "git@gitlab.com:org/repo.git",
        profilesGitRef: "main",
      })
    )

    // The wire carries URL and ref separately; the form collects a single
    // handle. We forward the URL alone — ref is empty in every seed and
    // the form has no ref picker yet.
    expect(row.gitProfileRepo).toBe("git@gitlab.com:org/repo.git")
  })

  it("keeps the absent-repo case as null, not an empty string", () => {
    const row = mapProjectViewToDetail(
      projectViewFixture({ profilesGitUrl: null })
    )

    expect(row.gitProfileRepo).toBeNull()
  })

  it("defaults the derived columns to the honest 'not measured yet' values", () => {
    const row = mapProjectViewToDetail(projectViewFixture())

    // ProjectView carries nothing about runs or cost — until the wire
    // joins those in, the screen has to know "not measured" from zeros
    // and a dash, not from a fabricated number.
    expect(row.activeRuns).toBe(0)
    expect(row.totalRuns).toBe(0)
    expect(row.spendToday).toBeNull()
  })
})

describe("mapProjectsPageToSummaries", () => {
  it("maps every wire row in the list", () => {
    const rows = mapProjectsPageToSummaries([
      projectViewFixture({ id: "id-1", slug: "vega" }),
      projectViewFixture({ id: "id-2", slug: "atlas" }),
    ])

    expect(rows).toHaveLength(2)
    expect(rows[0]?.id).toBe("id-1")
    expect(rows[0]?.slug).toBe("vega")
    expect(rows[1]?.slug).toBe("atlas")
  })

  it("returns an empty array for an empty list", () => {
    expect(mapProjectsPageToSummaries([])).toEqual([])
  })
})

describe("mapProjectSettingsViewToSettings", () => {
  it("carries every numeric and boolean field through verbatim", () => {
    const settings = mapProjectSettingsViewToSettings({
      projectId: "p_comuki",
      minIdle: 2,
      maxConcurrent: 12,
      idleTtlSeconds: 600,
      approveRequired: false,
      knowledgeEnabled: true,
      verifyEnabled: true,
      proxyEnabled: true,
      softBudgetUsdMicros: 100_000_000,
      hardBudgetUsdMicros: 200_000_000,
      updatedAt: "2026-09-04T00:00:00.000+00:00",
      version: 7,
    })

    expect(settings.projectId).toBe("p_comuki")
    expect(settings.minIdle).toBe(2)
    expect(settings.maxConcurrent).toBe(12)
    expect(settings.idleTtlSeconds).toBe(600)
    expect(settings.approveRequired).toBe(false)
    expect(settings.knowledgeEnabled).toBe(true)
    expect(settings.verifyEnabled).toBe(true)
    expect(settings.proxyEnabled).toBe(true)
    expect(settings.softBudgetUsdMicros).toBe(100_000_000)
    expect(settings.hardBudgetUsdMicros).toBe(200_000_000)
    expect(settings.version).toBe(7)
    expect(settings.updatedAt).toBe("2026-09-04T00:00:00.000+00:00")
  })

  it("keeps null fields as null — the panel renders 'platform default' for those", () => {
    const settings = mapProjectSettingsViewToSettings({
      projectId: "p_atlas",
      minIdle: 1,
      maxConcurrent: 4,
      idleTtlSeconds: null,
      approveRequired: true,
      knowledgeEnabled: false,
      verifyEnabled: false,
      proxyEnabled: false,
      softBudgetUsdMicros: null,
      hardBudgetUsdMicros: null,
      updatedAt: "2026-09-04T00:00:00.000+00:00",
      version: 1,
    })

    expect(settings.idleTtlSeconds).toBeNull()
    expect(settings.softBudgetUsdMicros).toBeNull()
    expect(settings.hardBudgetUsdMicros).toBeNull()
  })
})

describe("mapProjectSettingsToUpdateRequest", () => {
  it("round-trips every field and preserves the version token", () => {
    const body = mapProjectSettingsToUpdateRequest({
      projectId: "p_comuki",
      minIdle: 2,
      maxConcurrent: 12,
      idleTtlSeconds: 600,
      approveRequired: false,
      knowledgeEnabled: true,
      verifyEnabled: true,
      proxyEnabled: true,
      softBudgetUsdMicros: 100_000_000,
      hardBudgetUsdMicros: 200_000_000,
      version: 7,
      updatedAt: "2026-09-04T00:00:00.000+00:00",
    })

    expect(body.version).toBe(7)
    expect(body.minIdle).toBe(2)
    expect(body.maxConcurrent).toBe(12)
    expect(body.idleTtlSeconds).toBe(600)
    expect(body.approveRequired).toBe(false)
    expect(body.knowledgeEnabled).toBe(true)
    expect(body.verifyEnabled).toBe(true)
    expect(body.proxyEnabled).toBe(true)
    expect(body.softBudgetUsdMicros).toBe(100_000_000)
    expect(body.hardBudgetUsdMicros).toBe(200_000_000)
  })

  it("passes null through unchanged for the optional budget/idle fields", () => {
    const body = mapProjectSettingsToUpdateRequest({
      projectId: "p_atlas",
      minIdle: 1,
      maxConcurrent: 4,
      idleTtlSeconds: null,
      approveRequired: true,
      knowledgeEnabled: false,
      verifyEnabled: false,
      proxyEnabled: false,
      softBudgetUsdMicros: null,
      hardBudgetUsdMicros: null,
      version: 1,
      updatedAt: "2026-09-04T00:00:00.000+00:00",
    })

    expect(body.idleTtlSeconds).toBeNull()
    expect(body.softBudgetUsdMicros).toBeNull()
    expect(body.hardBudgetUsdMicros).toBeNull()
  })
})

describe("mapCostsPageToCostSummary", () => {
  it("divides the wire's USD-micros fields into USD and keeps the exceeded flags", () => {
    const summary = mapCostsPageToCostSummary({
      projectId: "p_comuki",
      spentUsdMicros: 12_345_678,
      softLimitUsdMicros: 50_000_000,
      hardLimitUsdMicros: 100_000_000,
      softExceeded: false,
      hardExceeded: false,
      recent: [],
    })

    expect(summary.projectId).toBe("p_comuki")
    expect(summary.spentUsd).toBeCloseTo(12.345678, 6)
    expect(summary.softBudgetUsd).toBeCloseTo(50, 6)
    expect(summary.hardBudgetUsd).toBeCloseTo(100, 6)
    expect(summary.softExceeded).toBe(false)
    expect(summary.hardExceeded).toBe(false)
    expect(summary.recent).toEqual([])
  })

  it("maps each entry of the recent feed and converts its cost to USD", () => {
    const summary = mapCostsPageToCostSummary({
      projectId: "p_plexor",
      spentUsdMicros: 4_200_000,
      softLimitUsdMicros: null,
      hardLimitUsdMicros: null,
      softExceeded: false,
      hardExceeded: false,
      recent: [
        {
          id: "00000000-0000-0000-0000-000000000001",
          runId: "00000000-0000-0000-0000-0000000000bb",
          source: "model",
          model: "claude-opus-4-5",
          inputTokens: 1024,
          outputTokens: 256,
          costUsdMicros: 4_200_000,
          occurredAt: "2026-09-04T10:00:00.000+00:00",
        },
      ],
    })

    expect(summary.recent).toHaveLength(1)
    expect(summary.recent[0]?.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(summary.recent[0]?.runId).toBe(
      "00000000-0000-0000-0000-0000000000bb"
    )
    expect(summary.recent[0]?.model).toBe("claude-opus-4-5")
    expect(summary.recent[0]?.inputTokens).toBe(1024)
    expect(summary.recent[0]?.outputTokens).toBe(256)
    expect(summary.recent[0]?.costUsd).toBeCloseTo(4.2, 6)
    expect(summary.recent[0]?.occurredAt).toBe("2026-09-04T10:00:00.000+00:00")
  })

  it("keeps null budget limits as null, not zero", () => {
    const summary = mapCostsPageToCostSummary({
      projectId: "p_atlas",
      spentUsdMicros: 0,
      softLimitUsdMicros: null,
      hardLimitUsdMicros: null,
      softExceeded: false,
      hardExceeded: false,
      recent: [],
    })

    expect(summary.softBudgetUsd).toBeNull()
    expect(summary.hardBudgetUsd).toBeNull()
  })

  it("tolerates a missing recent array rather than throwing", () => {
    const summary = mapCostsPageToCostSummary({
      projectId: "p_atlas",
      spentUsdMicros: 0,
      softLimitUsdMicros: null,
      hardLimitUsdMicros: null,
      softExceeded: false,
      hardExceeded: false,
      // kubb types are `any`, so a malformed wire row is possible until
      // the host grows explicit response schemas. We do not want the
      // costs panel to die on it.
      recent: undefined as unknown as never[],
    })

    expect(summary.recent).toEqual([])
  })
})

describe("mapCreateProjectInputToCreateRequest", () => {
  it("forwards name, slug and gitProfileRepo to the wire shape", () => {
    const body = mapCreateProjectInputToCreateRequest({
      name: "Vega",
      slug: "vega",
      gitProfileRepo: "git@github.com:org/repo.git",
    })

    expect(body.name).toBe("Vega")
    expect(body.slug).toBe("vega")
    expect(body.profilesGitUrl).toBe("git@github.com:org/repo.git")
    expect(body.profilesGitRef).toBeNull()
    expect(body.description).toBeNull()
  })

  it("carries a null repository as a null URL", () => {
    const body = mapCreateProjectInputToCreateRequest({
      name: "Atlas",
      slug: "atlas",
      gitProfileRepo: null,
    })

    expect(body.profilesGitUrl).toBeNull()
  })
})

describe("toProjectRow", () => {
  it("copies seed fields and zeroes the derived columns", () => {
    const row = toProjectRow({
      id: "p_vega",
      slug: "vega",
      name: "Vega",
      gitProfileRepo: null,
      createdAt: "2026-08-28",
    })

    expect(row.id).toBe("p_vega")
    expect(row.slug).toBe("vega")
    expect(row.name).toBe("Vega")
    expect(row.gitProfileRepo).toBeNull()
    expect(row.createdAt).toBe("2026-08-28")
    expect(row.activeRuns).toBe(0)
    expect(row.totalRuns).toBe(0)
    expect(row.spendToday).toBeNull()
  })
})
