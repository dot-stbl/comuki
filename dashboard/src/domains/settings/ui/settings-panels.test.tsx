import { render } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import { toSettingsSnapshot } from "@/domains/settings/api/mappers"
import { AppsPanel } from "@/domains/settings/ui/apps-panel"
import {
  createAppColumns,
  uniqueDeployTargets,
} from "@/domains/settings/ui/apps-columns"
import { KeysPanel } from "@/domains/settings/ui/keys-panel"
import { createProviderKeyColumns } from "@/domains/settings/ui/keys-columns"
import { RulesPanel } from "@/domains/settings/ui/rules-panel"
import { createRuleColumns } from "@/domains/settings/ui/rules-columns"
import { SETTINGS_SEED } from "@/shared/api/mock/settings.seed"
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

const find = (test: string) => document.querySelector(`[data-test="${test}"]`)

const says = (needle: string) =>
  (document.body.textContent ?? "").includes(needle)

const snapshot = toSettingsSnapshot(SETTINGS_SEED)

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
    expect(uniqueDeployTargets(snapshot.apps).map((option) => option.value))
      .toEqual(["Cloudflare", "Fly.io", "Vercel", "k8s"])
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
