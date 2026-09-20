/**
 * Composer history recall (issue #75) — the ↑/↓ navigator semantics
 * (pure) plus the host wiring through the REAL key path:
 *
 * - ↑ from an empty draft recalls the newest submitted message, ↑
 *   again walks older, ↓ walks back forward and past the newest
 *   restores the saved (empty) live draft;
 * - editing a recalled draft breaks recall — the next ↑ starts over
 *   from the newest entry;
 * - the arrows do not recall while the slash menu is open (the open
 *   surface owns ↑/↓).
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import {
  historyNavigator,
  LIVE_RECALL,
  recallAfterEdit,
  recallArrowsActive,
  stepRecall,
} from "./history"
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

describe("history navigator — pure position rules", () => {
  const items = ["oldest", "middle", "newest"]

  test("↑ from the live draft jumps to the newest entry; ↓ returns to the draft", () => {
    expect(historyNavigator(null, "older", items)).toBe(2)
    expect(historyNavigator(2, "newer", items)).toBeNull()
    expect(historyNavigator(null, "newer", items)).toBeNull()
  })

  test("↑ at the oldest stays put; ↓ walks forward one step at a time", () => {
    expect(historyNavigator(0, "older", items)).toBe(0)
    expect(historyNavigator(0, "newer", items)).toBe(1)
    expect(historyNavigator(1, "newer", items)).toBe(2)
  })

  test("empty history never leaves the live line; stale indexes clamp", () => {
    expect(historyNavigator(null, "older", [])).toBeNull()
    expect(historyNavigator(99, "older", items)).toBe(1)
  })

  test("stepRecall restores the saved draft past the newest entry", () => {
    const firstUp = stepRecall(LIVE_RECALL, "older", items)
    expect(firstUp.state.index).toBe(2)
    expect(firstUp.text).toBe("newest")

    const toMiddle = stepRecall(firstUp.state, "older", items)
    expect(toMiddle.text).toBe("middle")

    const toOldest = stepRecall(toMiddle.state, "older", items)
    expect(toOldest.text).toBe("oldest")

    // ↓ ↓ ↓: newest, then past it → the live draft returns.
    const down = stepRecall(toOldest.state, "newer", items)
    const downAgain = stepRecall(down.state, "newer", items)
    const downOut = stepRecall(downAgain.state, "newer", items)
    expect(downOut.state.index).toBeNull()
    expect(downOut.text).toBe(firstUp.state.savedDraft)
  })

  test("an unchanged step reports no text (the composer keeps what it has)", () => {
    const atOldest = { index: 0, savedDraft: "typed" }
    expect(stepRecall(atOldest, "older", items).text).toBeNull()
    const live = { index: null, savedDraft: "typed" }
    expect(stepRecall(live, "newer", items).text).toBeNull()
  })

  test("editing a recalled draft breaks recall; arrows only from empty/recalled", () => {
    const recalled = { index: 1, savedDraft: "" }
    const edited = recallAfterEdit(recalled, "edited text")
    expect(edited.index).toBeNull()
    expect(edited.savedDraft).toBe("edited text")

    expect(recallArrowsActive(LIVE_RECALL, "")).toBe(true)
    expect(recallArrowsActive(LIVE_RECALL, "half-typed")).toBe(false)
    expect(recallArrowsActive(recalled, "anything")).toBe(true)
  })
})

describe("tui host — history recall through the keymap", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let kernel: ClientKernel
  let conversation: ReturnType<typeof fakePorts>["conversation"]
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

  async function submitAndComplete(message: string): Promise<void> {
    host.setDraft(message)
    // Read the count BEFORE dispatch — the fake port registers the
    // submit synchronously inside the dispatch call.
    const count = conversation.submits.length
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > count)
    conversation.submits[conversation.submits.length - 1]!.resolve({
      messages: [],
      awaitingApproval: false,
    })
    await waitUntil(() => {
      const session = kernel.snapshot().state.sessions[0]
      return (
        session !== undefined &&
        session.identity.kind === "remote" &&
        session.turn.kind === "idle"
      )
    })
  }

  test("↑/↓ walk the submitted history and restore the live draft", async () => {
    await submitAndComplete("history one")
    await submitAndComplete("history two")

    setup.mockInput.pressArrow("up")
    await pumpInput()
    await waitUntil(() => host.getDraft() === "history two")

    setup.mockInput.pressArrow("up")
    await pumpInput()
    await waitUntil(() => host.getDraft() === "history one")

    setup.mockInput.pressArrow("down")
    await pumpInput()
    await waitUntil(() => host.getDraft() === "history two")

    setup.mockInput.pressArrow("down")
    await pumpInput()
    await waitUntil(() => host.getDraft() === "")
  })

  test("editing a recalled draft breaks recall — ↑ no longer fires on the edited draft", async () => {
    await submitAndComplete("history one")
    await submitAndComplete("history two")

    setup.mockInput.pressArrow("up")
    await pumpInput()
    await waitUntil(() => host.getDraft() === "history two")

    await setup.mockInput.typeText("!", 0)
    await waitUntil(() => host.getDraft() === "history two!")

    // The edited draft is neither empty nor a verbatim recalled
    // entry — ↑/↓ belong to the composer, not to recall.
    setup.mockInput.pressArrow("up")
    await pumpInput()
    await new Promise((resolve) => setTimeout(resolve, 80))
    expect(host.getDraft()).toBe("history two!")
  })

  test("arrows do not recall while the slash menu is open (menu owns ↑/↓)", async () => {
    await submitAndComplete("history one")

    await setup.mockInput.typeText("/", 0)
    await setup.waitForFrame((frame) => frame.includes("commands"))

    setup.mockInput.pressArrow("up")
    await pumpInput()
    // The menu selection moved (or wrapped) — the draft is still the
    // half-typed slash query, not a recalled history entry.
    expect(host.getDraft()).toBe("/")
    expect(host.getSurfaceKind()).toBe("slash-menu")
  })
})
