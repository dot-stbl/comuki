import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { toSettingsSnapshot } from "@/domains/settings/api/mappers"
import { AppsPanel } from "@/domains/settings/ui/apps-panel"
import {
  createAppColumns,
  uniqueDeployTargets,
} from "@/domains/settings/ui/apps-columns"
import { EditionPanel } from "@/domains/settings/ui/edition-panel"
import { KeysPanel } from "@/domains/settings/ui/keys-panel"
import { createProviderKeyColumns } from "@/domains/settings/ui/keys-columns"
import { RulesPanel } from "@/domains/settings/ui/rules-panel"
import { createRuleColumns } from "@/domains/settings/ui/rules-columns"
import { SETTINGS_SEED } from "@/shared/api/mock/settings.seed"
import {
  COMMUNITY_EDITION_SNAPSHOT,
  type EditionSnapshot,
} from "@/shared/editions/model"
import { applyDataFilters } from "@/shared/ui"

/* jsdom implements neither, and the virtualizer needs both: a ResizeObserver to
   watch the scroll port, and a scroll port with a height to decide how many
   rows are worth rendering. Without them the body renders nothing and every
   assertion below would pass against an empty table. Lifted from
   `shared/ui/data-table/data-table.test.tsx`, which is where they were worked
   out. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 960,
  })
})

/* EditionPanel reads `/api/v1/edition` through TanStack Query; mocking
   the hook — rather than standing up a QueryClient + transport — keeps
   the assertion focused on the panel's rendering rules. Three states
   matter here: Community (every paid feature locked), paid (multi-repo
   covered) so the gated affordance renders its non-fallback shape, and
   loading (the panel paints the loading hint, not the matrix). */
const edition = vi.hoisted(() => ({
  current: {
    data: undefined as EditionSnapshot | undefined,
    isPending: false,
    isError: false,
    refetch: vi.fn(),
  },
}))

vi.mock("@/shared/editions/queries", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@/shared/editions/queries")>()
  return {
    ...actual,
    useEdition: () => edition.current,
    // wireToSnapshot still flows through; we don't need to mock it.
  }
})

const find = (test: string) => document.querySelector(`[data-test="${test}"]`)

const says = (needle: string) =>
  (document.body.textContent ?? "").includes(needle)

const snapshot = toSettingsSnapshot(SETTINGS_SEED)

/* The Community mock re-used above — every paid feature unavailable, the
   only limit projects at 0/1 — is the dashboard's single source of truth
   for the mocked Community state. */

const PAID_EDITION_SNAPSHOT: EditionSnapshot = {
  ...COMMUNITY_EDITION_SNAPSHOT,
  tier: "team",
  status: "valid",
  features: COMMUNITY_EDITION_SNAPSHOT.features.map((row) =>
    row.key === "multi-repo" ? { ...row, available: true } : row
  ),
  limits: [{ key: "projects", current: 3, cap: 10 }],
  version: "0.42.0",
  expiresAt: "2099-12-31T00:00:00Z",
}

function mountEdition() {
  // The QueryClient exists only because useEdition's TanStack hook insists;
  // the data comes from the mock above, so the client is unused.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <EditionPanel />
    </QueryClientProvider>
  )
}

describe("the read-only sections say where they live", () => {
  it("names the client's git on the app registry, and renders its rows", () => {
    render(<AppsPanel apps={snapshot.apps} />)

    // Load-bearing product copy: a panel with no controls and no explanation
    // reads as a screen somebody forgot to finish, which is the wrong reading —
    // this one is complete.
    expect(
      says("read-only · the registry is declared in the client's git")
    ).toBe(true)
    // The rows are actually painted. Without the stubs above this passes
    // against nothing, which is the trap this file exists to avoid.
    expect(says("billing-api")).toBe(true)
    expect(find("apps-count")?.textContent).toBe(
      `${snapshot.apps.length} shown`
    )
  })

  it("names the commit on the rule set, and states the conflict reading first", () => {
    render(<RulesPanel rules={snapshot.rules} />)

    expect(
      says("read-only · rules live in the client's git and change by commit")
    ).toBe(true)
    // The reading the operator came for, above the table rather than under it.
    const conflicts = find("rules-conflicts")
    expect(conflicts?.getAttribute("data-tone")).toBe("ok")
    expect(conflicts?.textContent).toContain(
      `${snapshot.rules.length} active rules`
    )
  })

  it("names env and the proxy on the provider keys, and shows the reading that matters", () => {
    render(<KeysPanel keys={snapshot.keys} />)

    expect(
      says("read-only · keys come from env, rotation runs in the proxy")
    ).toBe(true)
    // The provider's own sentence reaches the screen rather than being
    // flattened to the enum behind it.
    expect(says("budget 67%")).toBe(true)
  })
})

describe("the edition panel renders the capability matrix and gates the multi-repo affordance", () => {
  it("paints the capability matrix AND the locked affordance under Community (matrix is not behind the gate)", () => {
    edition.current = {
      data: COMMUNITY_EDITION_SNAPSHOT,
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    }

    mountEdition()

    // The matrix renders every feature, including the paid ones, with the
    // lock mark — that is the honest reading a Community reader deserves.
    const matrix = find("edition-features")
    expect(matrix).not.toBeNull()
    const rows = matrix?.querySelectorAll("li") ?? []
    expect(rows.length).toBe(COMMUNITY_EDITION_SNAPSHOT.features.length)
    // Every paid feature's mark carries data-available="no".
    for (const row of Array.from(rows)) {
      const mark = row.querySelector("[data-available]")
      expect(mark?.getAttribute("data-available")).toBe("no")
    }

    // The multi-repo affordance is gated; under Community the gate closes
    // to the kit's locked fallback rather than rendering the action row.
    expect(find("edition-multirepo-affordance")).toBeNull()
    expect(find("feature-gate-locked")).not.toBeNull()
    // The locked fallback names the feature key it closed on.
    expect(says("multi-repo")).toBe(true)
  })

  it("paints the matrix AND the live affordance row when multi-repo is covered", () => {
    edition.current = {
      data: PAID_EDITION_SNAPSHOT,
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    }

    mountEdition()

    const matrix = find("edition-features")
    expect(matrix).not.toBeNull()
    // multi-repo's mark flips to yes now that the edition covers it; every
    // other paid feature stays locked.
    const multiRepoRow = Array.from(
      matrix?.querySelectorAll("li") ?? []
    ).find((row) => row.textContent?.includes("multi-repo"))
    expect(
      multiRepoRow
        ?.querySelector("[data-available]")
        ?.getAttribute("data-available")
    ).toBe("yes")

    // The gated affordance renders its real shape — presentational, no
    // live wiring — when the feature is covered.
    const affordance = find("edition-multirepo-affordance")
    expect(affordance).not.toBeNull()
    expect(affordance?.getAttribute("aria-disabled")).toBe("true")
    expect(find("feature-gate-locked")).toBeNull()
  })

  it("paints the loading hint while the snapshot is still in flight", () => {
    edition.current = {
      data: undefined,
      isPending: true,
      isError: false,
      refetch: vi.fn(),
    }

    mountEdition()

    expect(find("edition-loading")).not.toBeNull()
    // The matrix is not yet rendered — there is nothing to lock-mark.
    expect(find("edition-features")).toBeNull()
  })
})

describe("the filters a column declares match the fields they advertise", () => {
  it("filters apps across app, repo and stack — everything the placeholder says", () => {
    const columns = createAppColumns(uniqueDeployTargets(snapshot.apps))

    // The repository, which is not the column's own field.
    expect(
      applyDataFilters(snapshot.apps, { name: "comuki/docs" }, columns).map(
        (app) => app.name
      )
    ).toEqual(["docs-site"])
    // The stack, which is not the column's own field either.
    expect(
      applyDataFilters(snapshot.apps, { name: "Astro" }, columns).map(
        (app) => app.name
      )
    ).toEqual(["docs-site"])
    // And the select beside it narrows by exact value.
    expect(
      applyDataFilters(snapshot.apps, { deploy: "k8s" }, columns).map(
        (app) => app.name
      )
    ).toEqual(["worker-pool"])
  })

  it("offers only the deploy targets the registry actually names", () => {
    expect(
      uniqueDeployTargets(snapshot.apps).map((option) => option.value)
    ).toEqual(["Cloudflare", "Fly.io", "Vercel", "k8s"])
  })

  it("filters rules across id, scope and description", () => {
    const columns = createRuleColumns([])

    expect(
      applyDataFilters(snapshot.rules, { id: "web-app" }, columns).map(
        (rule) => rule.id
      )
    ).toEqual(["ui-tokens"])
    expect(
      applyDataFilters(snapshot.rules, { kind: "soft" }, columns).map(
        (rule) => rule.id
      )
    ).toEqual(["ui-tokens", "test-cov"])
  })

  it("filters provider keys across provider and scope", () => {
    const columns = createProviderKeyColumns()

    expect(
      applyDataFilters(snapshot.keys, { provider: "judge" }, columns).map(
        (key) => key.provider
      )
    ).toEqual(["provider-B"])
    expect(
      applyDataFilters(snapshot.keys, { status: "warn" }, columns).map(
        (key) => key.provider
      )
    ).toEqual(["proxy"])
  })
})
