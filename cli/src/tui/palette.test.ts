/**
 * Command palette + help view (issue #75) — ctrl+p opens the fuzzy
 * palette over the SAME named-command registry that drives slash and
 * keybindings:
 *
 * - ctrl+p opens; typing filters (query input owns typing); up/down
 *   move the selection; enter RUNS the selected command through the
 *   registry; esc closes and the composer takes focus back;
 * - while an approval card is up the palette is suppressed (the
 *   approval layer owns the keys);
 * - /help + dispatch("help") open the help view; esc closes it;
 * - the open palette owns ↑/↓ (history recall does not fire).
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
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

/** Let the renderer's async input pump process a synthetic key. */
async function pumpInput(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 20))
}

describe("tui host — command palette", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let kernel: ClientKernel
  let conversation: ReturnType<typeof fakePorts>["conversation"]
  let approval: ReturnType<typeof fakePorts>["approval"]
  let endFeed: () => void
  let host: TuiHost

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 80,
      height: 24,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    const ports = fakePorts()
    const feed = fakeFeed()
    endFeed = feed.end
    conversation = ports.conversation
    approval = ports.approval
    kernel = createClientKernel({ ports: ports.ports, feed: feed.port })
    kernel.start()
    host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
    })
    await setup.waitForVisualIdle()
  })

  afterEach(async () => {
    await host.destroy()
    kernel.stop()
    endFeed()
  })

  test("ctrl+p chord opens the palette; typing filters; esc closes and restores the composer", async () => {
    setup.mockInput.pressKey("p", { ctrl: true })
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("command palette"))
    expect(host.getSurfaceKind()).toBe("palette")

    // The query input owns typing — filtering the list live.
    await setup.mockInput.typeText("help", 0)
    await setup.waitForFrame(
      (frame) => frame.includes("> Help") && !frame.includes("New session")
    )

    setup.mockInput.pressEscape()
    await pumpInput()
    await setup.waitForFrame((frame) => !frame.includes("command palette"))
    expect(host.getSurfaceKind()).toBe("none")

    // The composer owns typing again.
    await setup.mockInput.typeText("x", 0)
    await waitUntil(() => host.getDraft() === "x")
  })

  test("up/down move the selection; enter runs the selected command", async () => {
    setup.mockInput.pressKey("p", { ctrl: true })
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("> New session"))

    setup.mockInput.pressArrow("down")
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("> Exit"))

    setup.mockInput.pressArrow("up")
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("> New session"))

    // Enter RUNS new-session — a pending session opens.
    setup.mockInput.pressEnter()
    await pumpInput()
    await waitUntil(() => kernel.snapshot().state.sessions.length > 0)
    expect(kernel.snapshot().state.sessions[0]!.identity.kind).toBe("pending")
    expect(host.getSurfaceKind()).toBe("none")
  })

  test("a query with no matches shows the empty hint", async () => {
    setup.mockInput.pressKey("p", { ctrl: true })
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("command palette"))

    await setup.mockInput.typeText("zzzz", 0)
    await setup.waitForFrame((frame) => frame.includes("no matching commands"))
  })

  test("the palette is suppressed while an approval card is up", async () => {
    host.setDraft("ask for approval")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)
    conversation.submits[0]!.resolve({
      messages: [],
      awaitingApproval: true,
      pendingPlan: { planSteps: ["TUI-PALETTE-STEP"] },
    })
    await setup.waitForFrame((frame) => frame.includes("decide:"))

    // The named command is inert while the card is up…
    expect(host.keymap.dispatch("open-palette")).toBe(false)
    // …and so is the physical chord.
    setup.mockInput.pressKey("p", { ctrl: true })
    await pumpInput()
    expect(host.getSurfaceKind()).toBe("none")
    const frame = setup.captureCharFrame()
    expect(frame).not.toContain("command palette")
    expect(frame).toContain("decide:")

    // The card itself still decides through its own layer.
    expect(host.keymap.dispatch("approve")).toBe(true)
    await waitUntil(() => approval.decisions.length > 0)
  })

  test("help lists every registered command; esc closes", async () => {
    expect(host.keymap.dispatch("help")).toBe(true)
    await setup.waitForFrame((frame) => frame.includes("/exit [quit,q]"))
    const frame = setup.captureCharFrame()
    expect(frame).toContain("ctrl+n")
    expect(frame).toContain("ctrl+p")
    expect(frame).toContain("ctrl+e")

    setup.mockInput.pressEscape()
    await pumpInput()
    await setup.waitForFrame((frame) => !frame.includes("/exit [quit,q]"))
    expect(host.getSurfaceKind()).toBe("none")
  })

  test("the open palette owns up/down — history recall does not fire", async () => {
    // Seed history: two submitted turns.
    host.setDraft("history one")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)
    conversation.submits[0]!.resolve({ messages: [], awaitingApproval: false })
    await waitUntil(() =>
      kernel.snapshot().state.sessions.some(
        (session) =>
          session.turn.kind === "idle" &&
          (session.history?.length ?? 0) > 0
      )
    )

    setup.mockInput.pressKey("p", { ctrl: true })
    await pumpInput()
    await setup.waitForFrame((frame) => frame.includes("> New session"))

    setup.mockInput.pressArrow("up")
    await pumpInput()
    // The selection wrapped (n entries → last), the composer draft
    // did NOT become a recalled history entry. Issue #78 added
    // `swarm-canvas-inspect` at the end of the registry; the wrap
    // lands there now.
    await setup.waitForFrame(
      (frame) => frame.includes("> Inspect swarm item")
    )
    expect(host.getDraft()).toBe("")
  })
})
