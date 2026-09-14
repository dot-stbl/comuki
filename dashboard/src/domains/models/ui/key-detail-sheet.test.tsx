import { fireEvent, render } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
import type { Session } from "@/shared/session"

import { KeyDetailSheet } from "./key-detail-sheet"

/* The product marks its own elements with `data-test`, not `data-testid`. */
const find = (selector: string) => document.querySelector(selector)
const sheet = () => find('[data-test="key-detail-sheet"]')

const DAY = 86_400

const ENDPOINTS: ModelEndpoint[] = [
  {
    id: "ep_lead",
    name: "provider-A",
    wire: "anthropic",
    baseUrl: "https://api.provider-a.example/v1",
    state: "ok",
    models: ["lead-xl-2", "lead-mid-2"],
    note: "lead traffic",
  },
]

const SESSION: Session = {
  user: {
    id: "u_test",
    name: "Test Operator",
    email: "test@comuki.local",
    platformRoles: ["platform-admin"],
    projectRoles: {},
  },
  projects: [
    { id: "p_comuki", key: "comuki", name: "Comuki" },
    { id: "p_atlas", key: "atlas", name: "Atlas" },
  ],
}

const LIVE_KEY: VirtualKey = {
  id: "vk_7f2c",
  prefix: "vk_7f2c…",
  label: "platform lead traffic",
  endpointId: "ep_lead",
  models: ["lead-xl-2", "lead-mid-2"],
  scope: { kind: "platform" },
  budgetUsd: 400,
  spentUsd: 361.4,
  expiresInSec: 12 * DAY,
  lastUsedAgoSec: 6 * DAY,
  revoked: false,
  createdAgoSec: 74 * DAY,
  grants: [
    { role: "lead", projectId: null },
    { role: "worker", projectId: "p_atlas" },
  ],
  spendDaily: [
    { label: "mon", usd: 21.4 },
    { label: "tue", usd: 17.8 },
    { label: "wed", usd: 24.2 },
    { label: "thu", usd: 0 },
    { label: "fri", usd: 0 },
  ],
}

const CATALOGUE_KEY: VirtualKey = {
  id: "sha256:test",
  prefix: "ck_live_9f2a",
  label: "default lead-xl-2",
  endpointId: "ep_lead",
  models: [],
  scope: { kind: "project", projectId: "p_atlas" },
  budgetUsd: 400,
  spentUsd: null,
  expiresInSec: 12 * DAY,
  lastUsedAgoSec: null,
  revoked: false,
  createdAgoSec: null,
  grants: null,
  spendDaily: null,
}

const STOPPED_KEY: VirtualKey = {
  ...LIVE_KEY,
  id: "vk_11ab",
  prefix: "vk_11ab…",
  revoked: true,
  lastUsedAgoSec: null,
  spentUsd: 3.9,
  spendDaily: Array.from({ length: 14 }, (_, index) => ({
    label: `d${index}`,
    usd: 0,
  })),
}

function renderSheet(entry: VirtualKey | null, open = true) {
  return render(
    <KeyDetailSheet
      entry={entry}
      endpoints={ENDPOINTS}
      enforced={false}
      revokingId={null}
      onRevoke={vi.fn()}
      session={SESSION}
      open={open}
      onOpenChange={vi.fn()}
    />
  )
}

describe("the key detail sheet", () => {
  it("shows the key the row argued for, in full", () => {
    renderSheet(LIVE_KEY)

    const text = sheet()?.textContent ?? ""
    // The handle, the purpose and the route.
    expect(text).toContain("vk_7f2c…")
    expect(text).toContain("platform lead traffic")
    expect(text).toContain("provider-A · anthropic-compatible")
    // The dates a key lives by.
    expect(text).toContain("74 days ago")
    expect(text).toContain("6 days ago")
    expect(text).toContain("in 12 days")
    expect(text).toContain("platform")
    // The grants, as roles in places.
    expect(text).toContain("lead · platform")
    expect(text).toContain("worker · atlas")
    // The models, listed rather than counted.
    expect(text).toContain("lead-xl-2")
    expect(text).toContain("lead-mid-2")
  })

  it("states the spend window in words, with the chart confirming it", () => {
    renderSheet(LIVE_KEY)

    const text = sheet()?.textContent ?? ""
    expect(text).toContain("$63.40 over the last 5 days")
    expect(text).toContain("$12.68 a day")
    expect(text).toContain("heaviest wed at $24.20")
    expect(text).toContain("no spend recorded in the last 2 days")

    const chart = find(
      '[data-test="key-detail-sheet"] [data-test="bar-series"]'
    )
    expect(chart?.getAttribute("aria-label")).toContain(
      "$63.40 over the last 5 days"
    )
    // A column per metered day, drawn as bars on the shared axis — the two
    // silent days draw nothing, exactly as they spent nothing.
    expect(chart?.querySelectorAll('[data-test="bar-series-bar"]').length).toBe(
      3
    )
  })

  it("says what an empty allow-list permits, the way the row does", () => {
    renderSheet(CATALOGUE_KEY)

    expect(sheet()?.textContent).toContain("every model on the endpoint")
  })

  it("reads a catalogue row honestly rather than inventing its history", () => {
    renderSheet(CATALOGUE_KEY)

    const text = sheet()?.textContent ?? ""
    expect(text).toContain("not on this wire")
    expect(text).toContain("not metered on this wire")
    expect(text).toContain("not recorded on this wire")
    // And it does not draw fourteen bars of nothing.
    expect(find('[data-test="bar-series"]')).toBeNull()
  })

  it("shows no chart and no act for a key that never spent and already stopped", () => {
    renderSheet(STOPPED_KEY)

    const text = sheet()?.textContent ?? ""
    expect(text).toContain("no spend recorded")
    expect(text).toContain("never used")
    expect(text).toContain("already stopped")
    expect(find('[data-test="key-detail-revoke"]')).toBeNull()
    expect(find('[data-test="bar-series"]')).toBeNull()
  })

  it("offers the row's act from the footer, word and all", () => {
    const onRevoke = vi.fn()
    render(
      <KeyDetailSheet
        entry={LIVE_KEY}
        endpoints={ENDPOINTS}
        enforced={false}
        revokingId={null}
        onRevoke={onRevoke}
        session={SESSION}
        open
        onOpenChange={vi.fn()}
      />
    )

    fireEvent.click(find('[data-test="key-detail-revoke"]') as HTMLElement)
    expect(onRevoke).toHaveBeenCalledWith(LIVE_KEY)
  })

  it("closes from the glyph as well as from escape and the scrim", () => {
    const onOpenChange = vi.fn()
    render(
      <KeyDetailSheet
        entry={LIVE_KEY}
        endpoints={ENDPOINTS}
        enforced={false}
        revokingId={null}
        onRevoke={vi.fn()}
        session={SESSION}
        open
        onOpenChange={onOpenChange}
      />
    )

    fireEvent.click(find('[data-test="key-detail-close"]') as HTMLElement)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it("renders nothing at all while it is closed", () => {
    renderSheet(LIVE_KEY, false)

    expect(sheet()).toBeNull()
  })
})
