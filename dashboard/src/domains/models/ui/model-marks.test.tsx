import { useMemo } from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
import { createEndpointColumns } from "@/domains/models/ui/endpoint-columns"
import { createKeyColumns, getKeyId } from "@/domains/models/ui/key-columns"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { BRAND_IDS, DataTable, isBrandId } from "@/shared/ui"

/* The virtualizer needs a scroll port with a depth and something watching it,
   and jsdom has neither — without these the body renders no rows at all and
   every assertion below would pass by looking at an empty table. Same stubs as
   `data-table.test.tsx`, for the same reason. */
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
    value: 1200,
  })
})

/**
 * Two upstreams on the same wire and only one of them is anybody's product.
 *
 * This pair is the whole argument. `ep_self` speaks the `openai` wire and is a
 * url on the cluster's own network; drawing a vendor's mark on that row would
 * assert a commercial relationship it does not have. The seed ships exactly
 * this shape for exactly this reason.
 */
const ENDPOINTS: ModelEndpoint[] = [
  {
    id: "ep_vendor",
    name: "provider-B",
    wire: "openai",
    baseUrl: "https://api.provider-b.example/v1",
    state: "ok",
    models: ["worker-sm-4"],
    note: "worker steps",
  },
  {
    id: "ep_self",
    name: "self-hosted",
    wire: "openai",
    baseUrl: "http://vllm.comuki.internal:8000/v1",
    state: "degraded",
    models: ["worker-sm-oss"],
    note: "p95 four times the others",
  },
  {
    id: "ep_lead",
    name: "provider-A",
    wire: "anthropic",
    baseUrl: "https://api.provider-a.example/v1",
    state: "ok",
    models: ["lead-xl-2"],
    note: "lead traffic",
  },
]

function Endpoints() {
  const columns = useMemo(() => createEndpointColumns(), [])
  return (
    <DataTable
      columns={columns}
      data={ENDPOINTS}
      getRowId={(endpoint) => endpoint.id}
      density="compact"
    />
  )
}

describe("a wire is not a vendor, so the registry draws no vendor mark", () => {
  it("says the protocol in words on every row, including the self-hosted one", () => {
    render(<Endpoints />)

    const wires = Array.from(
      document.querySelectorAll('[data-test="wire-badge"]')
    )
    expect(wires.map((wire) => wire.textContent)).toEqual([
      "openai",
      "openai",
      "anthropic",
    ])

    // The row that makes the case: a vLLM url on the cluster's own network,
    // sitting on the `openai` wire and belonging to nobody. Two rows, one wire,
    // and no honest logo covers both.
    expect(
      screen.getByText("http://vllm.comuki.internal:8000/v1")
    ).not.toBeNull()
    expect(document.querySelector('[data-test="brand-icon"]')).toBeNull()
    expect(document.querySelector('[data-test="brand-tag"]')).toBeNull()
  })

  it("keeps the two wire names out of the mark registry entirely", () => {
    // Not a rendering detail — a rule. Adding `openai` or `anthropic` to the
    // registry is what would let some future column quietly draw one.
    expect(isBrandId("openai")).toBe(false)
    expect(isBrandId("anthropic")).toBe(false)
    expect(BRAND_IDS).not.toContain("openai")
    expect(BRAND_IDS).not.toContain("anthropic")
  })
})

const LIVE_KEY: VirtualKey = {
  id: "vk_7f2c",
  prefix: "vk_7f2c…",
  label: "platform lead traffic",
  endpointId: "ep_lead",
  models: ["lead-xl-2"],
  scope: { kind: "platform" },
  budgetUsd: 400,
  spentUsd: 120,
  expiresInSec: 12 * 86_400,
  lastUsedAgoSec: 600,
  revoked: false,
}

function Keys({ onRevoke }: { onRevoke: () => void }) {
  const session = useSession()
  const columns = useMemo(
    () =>
      createKeyColumns({
        endpoints: ENDPOINTS,
        // The proxy is off in the seed and the caps are not being applied; the
        // revoke act is the one thing on this row that still works either way.
        enforced: false,
        revokingId: null,
        onRevoke,
        session,
      }),
    [session, onRevoke]
  )

  return (
    <DataTable
      columns={columns}
      data={[LIVE_KEY]}
      getRowId={getKeyId}
      density="compact"
    />
  )
}

function mountKeys(roles: Role[]) {
  const onRevoke = vi.fn()
  render(
    <TestSession roles={roles}>
      <Keys onRevoke={onRevoke} />
    </TestSession>
  )
  return {
    onRevoke,
    revoke: () => screen.getByRole("button", { name: "Revoke vk_7f2c…" }),
  }
}

/** Focus rather than hover — see `sources-marks.test.tsx` for why. */
async function tabTo(
  user: ReturnType<typeof userEvent.setup>,
  target: HTMLElement
) {
  for (let step = 0; step < 40; step += 1) {
    if (document.activeElement === target) return
    await user.tab()
  }
  throw new Error("the control never took focus")
}

describe("the one icon-only act on the key table", () => {
  it("hands the word back on focus, not only on hover", async () => {
    const user = userEvent.setup()
    const { revoke } = mountKeys(["platform-admin"])

    expect(screen.queryByRole("tooltip")).toBeNull()

    await tabTo(user, revoke())

    expect((await screen.findByRole("tooltip")).textContent).toBe(
      "Revoke this key"
    )
    // Described, never named: a key is still revoked *by its prefix*, which is
    // the only part of it that is ever displayed again.
    expect(revoke().getAttribute("aria-label")).toBe("Revoke vk_7f2c…")
    expect(revoke().getAttribute("title")).toBeNull()
  })

  it("puts a refusal in the same place the label would have been", async () => {
    const user = userEvent.setup()
    const { revoke, onRevoke } = mountKeys(["viewer"])

    expect(revoke().getAttribute("aria-disabled")).toBe("true")
    expect(revoke().hasAttribute("disabled")).toBe(false)

    await tabTo(user, revoke())

    expect((await screen.findByRole("tooltip")).textContent).toBe(
      "needs operator or platform-admin"
    )

    await user.keyboard("{Enter}")
    expect(onRevoke).not.toHaveBeenCalled()
  })
})
