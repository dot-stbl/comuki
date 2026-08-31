import { useMemo } from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type { WorkerVersion } from "@/domains/compute/model/types"
import { ProviderKindMark } from "@/domains/compute/ui/compute-badges"
import {
  createVersionColumns,
  getVersionId,
} from "@/domains/compute/ui/version-columns"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { DataTable } from "@/shared/ui"

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

describe("a compute backend is shown as its mark", () => {
  it("draws both of v1's backends and names each one", () => {
    render(
      <>
        <ProviderKindMark kind="docker" />
        <ProviderKindMark kind="kubernetes" />
      </>
    )

    // The word did not go anywhere — it stopped being the thing on screen.
    expect(screen.getByRole("img", { name: "docker" })).not.toBeNull()
    expect(screen.getByRole("img", { name: "kubernetes" })).not.toBeNull()

    const marks = Array.from(
      document.querySelectorAll('[data-test="brand-tag"]')
    )
    expect(marks.map((mark) => mark.getAttribute("data-brand"))).toEqual([
      "docker",
      "kubernetes",
    ])
    // A hover reading for the operator who does not recognise a monochrome
    // whale at thirteen pixels, which is most operators most of the time.
    expect(marks[0].getAttribute("title")).toBe("docker")
  })

  it("carries no badge chrome, because a kind has no state to carry", () => {
    render(<ProviderKindMark kind="docker" />)

    // The kind used to be a bordered chip with a stand-in glyph and the word
    // beside it. A hairline box around a logo is a container built for a fact
    // that has nothing to put in it.
    const mark = document.querySelector('[data-test="brand-tag"]')
    expect(mark?.textContent).toBe("")
  })
})

const TARGET: WorkerVersion = {
  digest: "sha256:9f11",
  profilesRef: "profiles@v14",
  target: true,
  workers: 6,
  idle: 2,
  oldestUpSec: 900,
  providerIds: ["cp_docker"],
}

const STALE: WorkerVersion = {
  digest: "sha256:41ac",
  profilesRef: "profiles@v13",
  target: false,
  workers: 4,
  idle: 3,
  oldestUpSec: 7200,
  providerIds: ["cp_docker"],
}

function Versions({ onRetire }: { onRetire: () => void }) {
  const session = useSession()
  const columns = useMemo(
    () =>
      createVersionColumns({
        target: TARGET,
        retiringLabel: null,
        onRetire,
        session,
      }),
    [session, onRetire]
  )

  return (
    <DataTable
      columns={columns}
      data={[TARGET, STALE]}
      getRowId={getVersionId}
      density="compact"
    />
  )
}

function mountVersions(roles: Role[]) {
  const onRetire = vi.fn()
  render(
    <TestSession roles={roles}>
      <Versions onRetire={onRetire} />
    </TestSession>
  )
  return {
    onRetire,
    retire: () =>
      screen.getByRole("button", {
        name: "Retire 3 idle workers on sha256:41ac · profiles@v13",
      }),
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

describe("the one icon-only act on the rollout table", () => {
  it("hands the word back on focus, not only on hover", async () => {
    const user = userEvent.setup()
    const { retire } = mountVersions(["platform-admin"])

    expect(screen.queryByRole("tooltip")).toBeNull()

    await tabTo(user, retire())

    // Described, never named: the control keeps the name a person was told to
    // look for whether or not a pointer is anywhere near it.
    expect((await screen.findByRole("tooltip")).textContent).toBe(
      "Retire the idle workers on this label"
    )
    expect(retire().getAttribute("aria-describedby")).not.toBeNull()
    expect(retire().getAttribute("title")).toBeNull()
  })

  it("puts a refusal in the same place the label would have been", async () => {
    const user = userEvent.setup()
    const { retire, onRetire } = mountVersions(["viewer"])

    // `denied`, not `disabled`: a disabled control fires no pointer events, so
    // the sentence explaining the refusal would exist and be unreachable.
    expect(retire().getAttribute("aria-disabled")).toBe("true")
    expect(retire().hasAttribute("disabled")).toBe(false)

    await tabTo(user, retire())

    expect((await screen.findByRole("tooltip")).textContent).toBe(
      "needs operator or platform-admin"
    )

    await user.keyboard("{Enter}")
    expect(onRetire).not.toHaveBeenCalled()
  })
})
