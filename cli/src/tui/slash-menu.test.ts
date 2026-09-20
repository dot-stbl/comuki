/**
 * Slash command surface (issue #75) — the `/` completion menu over
 * the named-command registry, driven through the REAL key path
 * (physical typing + arrows + enter on the test renderer):
 *
 * - typing `/` opens the filterable menu; typing filters it;
 * - enter/tab COMPLETE the selection into the composer (they do not
 *   submit — the open menu owns enter); esc closes the menu;
 * - a submitted `/command` dispatches through the registry: /new,
 *   /q alias, /rename &lt;title&gt;, /clear-draft, /approve while a
 *   card is up;
 * - an unavailable command (/stop while idle) is inert and keeps the
 *   draft.
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import type { HarnessEffectPorts } from "../harness/effect-runner"
import { createTuiHost, type TuiHost } from "./host"

async function waitUntil(
  predicate: () => boolean,
  timeoutMs = 5_000
): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (!predicate()) {
    if (Date.now() > deadline) {
      throw new Error("condition not met before timeout")
    }
    await new Promise((resolve) => setTimeout(resolve, 10))
  }
}

/**
 * Give the renderer's async input pump a beat after a synthetic key
 * press — waitForFrame otherwise sees an idle scheduler before the
 * keystroke has been processed.
 */
async function pumpInput(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 20))
}

interface Harness {
  readonly kernel: ClientKernel
  readonly ports: HarnessEffectPorts & {
    readonly conversation: {
      readonly submits: readonly {
        readonly message: string
        resolve: (outcome: unknown) => void
        reject: (error: unknown) => void
      }[]
    }
    readonly approval: {
      readonly decisions: readonly {
        readonly approved: boolean
        resolve: (outcome: unknown) => void
      }[]
    }
  }
  readonly endFeed: () => void
}

function makeKernel(): Harness {
  const ports = fakePorts()
  const feed = fakeFeed()
  const kernel = createClientKernel({ ports: ports.ports, feed: feed.port })
  kernel.start()
  return {
    kernel,
    ports: ports as unknown as Harness["ports"],
    endFeed: feed.end,
  }
}

describe("tui host — slash completion menu", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let harness: Harness
  let host: TuiHost

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 80,
      height: 24,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    harness = makeKernel()
    host = await createTuiHost(harness.kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
    })
    await setup.waitForVisualIdle()
  })

  afterEach(async () => {
    await host.destroy()
    harness.kernel.stop()
    harness.endFeed()
  })

  test("typing / opens the menu; typing filters; esc closes; typing reopens", async () => {
    await setup.mockInput.typeText("/", 0)
    await setup.waitForFrame((frame) => frame.includes("/new") && frame.includes("/exit"))
    expect(host.getSurfaceKind()).toBe("slash-menu")

    await setup.mockInput.typeText("e", 0)
    await setup.waitForFrame(
      (frame) => frame.includes("/exit") && !frame.includes("/new")
    )
    const filtered = setup.captureCharFrame()
    expect(filtered).not.toContain("/new")
    expect(filtered).not.toContain("/help")

    setup.mockInput.pressEscape()
    await pumpInput()
    await setup.waitForFrame((frame) => !frame.includes("/exit"))
    expect(host.getSurfaceKind()).toBe("none")

    // Backspacing to a fresh query reopens the menu (dismissal lifts
    // on the next edit).
    setup.mockInput.pressBackspace()
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("/exit"))
    expect(host.getSurfaceKind()).toBe("slash-menu")
  })

  test("arrows move the selection; enter/tab complete — the open menu owns enter (no submit)", async () => {
    await setup.mockInput.typeText("/", 0)
    await setup.waitForFrame((frame) => frame.includes("/new"))

    setup.mockInput.pressArrow("down")
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("> /exit"))

    // Enter COMPLETES the highlighted /exit into the composer and
    // does NOT submit — the kernel never sees a turn.
    setup.mockInput.pressEnter()
    await pumpInput()
    await setup.waitForFrame((frame) => !frame.includes("> /exit"))
    expect(host.getDraft()).toBe("/exit ")
    expect(host.getSurfaceKind()).toBe("none")
    expect(harness.ports.conversation.submits.length).toBe(0)
  })

  test("/new executes through the registry and opens a pending session", async () => {
    await setup.mockInput.typeText("/new", 0)
    await setup.waitForFrame((frame) => frame.includes("/new"))
    // Complete "/new " first (menu owns enter), then submit.
    setup.mockInput.pressEnter()
    await setup.waitForFrame((frame) => !frame.includes("> /new"))
    setup.mockInput.pressEnter()
    await waitUntil(() =>
      harness.kernel.snapshot().state.sessions.length > 0
    )
    expect(harness.kernel.snapshot().state.sessions[0]!.identity.kind).toBe("pending")
  })

  test("/q alias exits the host through the registry", async () => {
    host.setDraft("/q")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => host.isClosed())
    expect(host.isKernelStopped()).toBe(true)
  })

  test("/rename <title> renames the active session", async () => {
    expect(host.keymap.dispatchByKeymap("ctrl+n")).toBe(true)
    await waitUntil(() =>
      harness.kernel.snapshot().state.sessions.length > 0
    )

    host.setDraft("/rename My Session")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => {
      const state = harness.kernel.snapshot().state
      return state.sessions[0]?.title === "My Session"
    })
    expect(host.getDraft()).toBe("")
  })

  test("/clear-draft empties the composer", async () => {
    host.setDraft("leftover draft")
    host.setDraft("/clear-draft")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    expect(host.getDraft()).toBe("")
  })

  test("/stop while idle is inert and keeps the draft (availability enforced)", async () => {
    host.setDraft("/stop")
    expect(host.keymap.dispatchByKeymap("return")).toBe(false)
    expect(host.getDraft()).toBe("/stop")
    expect(harness.ports.conversation.submits.length).toBe(0)
  })

  test("/approve decides the awaiting approval through the registry", async () => {
    host.setDraft("ask for approval")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => harness.ports.conversation.submits.length > 0)
    harness.ports.conversation.submits[0]!.resolve({
      messages: [],
      awaitingApproval: true,
      pendingPlan: { planSteps: ["TUI-SLASH-STEP"] },
    })
    await setup.waitForFrame((frame) => frame.includes("decide:"))

    host.setDraft("/approve")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => harness.ports.approval.decisions.length > 0)
    expect(harness.ports.approval.decisions[0]!.approved).toBe(true)
  })
})
