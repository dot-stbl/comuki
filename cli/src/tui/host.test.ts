/**
 * Host frame tests — the production OpenTUI host rendering from REAL
 * kernel snapshots driven by FAKE ports (issue #73).
 *
 * Proves, through `captureCharFrame` on the real test renderer:
 * - the geometry sweep 160x50 → 80x24 → 48x16 keeps chrome +
 *   transcript + composer on screen and preserves the draft on ONE
 *   host (compact layout kicks in at 48x16);
 * - the transcript is a pure function of kernel state: submit echo,
 *   streaming live text, authoritative completion, failed-turn alert;
 * - physical typing + enter submits through the keymap (the exact
 *   path a user's keystroke takes).
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import type { HarnessEffectPorts } from "../harness/effect-runner"
import { createTuiHost, type TuiHost } from "./host"

const DRAFT = "TUI-DRAFT-PRESERVED"
const GEOMETRIES = [
  [160, 50],
  [80, 24],
  [48, 16],
] as const

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
  readonly pushChunk: (sessionId: string, text: string) => void
  readonly stopFeed: () => void
}

function makeKernel(): Harness {
  const ports = fakePorts()
  const feed = fakeFeed()
  const kernel = createClientKernel({ ports: ports.ports, feed: feed.port })
  kernel.start()
  return {
    kernel,
    ports: ports as unknown as Harness["ports"],
    pushChunk(sessionId, text) {
      feed.push({
        kind: "chunk",
        sessionId,
        seq: Date.now() + Math.floor(Math.random() * 1000),
        text,
        receivedAtUnixMs: Date.now(),
      })
    },
    stopFeed() {
      feed.end()
    },
  }
}

describe("tui host — geometry sweep on one host (single renderer, single draft)", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let harness: Harness
  let host: TuiHost

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 160,
      height: 50,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    harness = makeKernel()
    host = await createTuiHost(harness.kernel, {
      renderer: setup.renderer,
      width: 160,
      height: 50,
      memoryMode: true,
    })
    await setup.waitForVisualIdle()
  })

  afterEach(async () => {
    await host.destroy()
    harness.kernel.stop()
    harness.stopFeed()
  })

  test("draft survives 160x50 → 80x24 → 48x16 with chrome + transcript + composer", async () => {
    // Seed a transcript through the kernel: submit + authoritative
    // completion, so the sweep asserts a populated viewport.
    host.setDraft("TUI-SWEEP-MSG")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => harness.ports.conversation.submits.length > 0)
    harness.ports.conversation.submits[0]!.resolve({
      messages: [
        {
          id: "m-sweep",
          role: "assistant",
          content: "TUI-SWEEP-ANSWER",
          createdAtUnixMs: 0,
        },
      ],
      awaitingApproval: false,
    })
    await setup.waitForFrame((frame) => frame.includes("TUI-SWEEP-ANSWER"))

    // One setDraft for the whole sweep — no per-resize re-injection.
    host.setDraft(DRAFT)
    await setup.waitForVisualIdle()

    for (const [w, h] of GEOMETRIES) {
      await host.setSize(w, h)
      await setup.waitForVisualIdle()
      const frame = setup.captureCharFrame()

      // chrome — full at regular sizes, compacted (comuki·…) below 60 cols
      const chromePresent =
        frame.includes("comuki · opentui (core)") || frame.includes("comuki·")
      expect(chromePresent).toBe(true)

      // transcript — the sticky-bottom viewport keeps the tail
      expect(frame).toContain("TUI-SWEEP-ANSWER")

      // composer — the same draft set ONCE before the first resize
      expect(frame).toContain(DRAFT)
    }
  })
})

describe("tui host — transcript renders from kernel snapshots (fake ports)", () => {
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
    harness.stopFeed()
  })

  test("submit echo appears; stream updates live text; completion replaces it", async () => {
    host.setDraft("TUI-ECHO-MSG")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)

    // The turn-queued echo renders as soon as the pending session opens.
    await setup.waitForFrame((frame) => frame.includes("you › TUI-ECHO-MSG"))

    // The pending session adopted server-1 and the submit is in flight.
    await waitUntil(() => harness.ports.conversation.submits.length > 0)

    // A streaming chunk rides the thinking turn as live text.
    harness.pushChunk("server-1", "TUI-LIVE-TEXT")
    await setup.waitForFrame((frame) => frame.includes("TUI-LIVE-TEXT"))

    // REST-authoritative completion replaces the live text with the
    // final message and settles the turn.
    harness.ports.conversation.submits[0]!.resolve({
      messages: [
        {
          id: "m-final",
          role: "assistant",
          content: "TUI-FINAL-ANSWER",
          createdAtUnixMs: 0,
        },
      ],
      awaitingApproval: false,
    })
    await setup.waitForFrame((frame) => frame.includes("comuki › TUI-FINAL-ANSWER"))
    expect(setup.captureCharFrame()).not.toContain("TUI-LIVE-TEXT")
  })

  test("a failed turn renders one alert line with the error message", async () => {
    host.setDraft("TUI-FAIL-MSG")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => harness.ports.conversation.submits.length > 0)

    harness.ports.conversation.submits[0]!.reject(new Error("TUI-BOOM"))
    await setup.waitForFrame((frame) =>
      frame.includes("× turn failed:") && frame.includes("TUI-BOOM")
    )
  })

  test("physical typing + enter submits through the keymap (the real key path)", async () => {
    await setup.mockInput.typeText("TUI-TYPED-MSG", 0)
    setup.mockInput.pressEnter()
    await waitUntil(() => harness.ports.conversation.submits.length > 0)
    expect(harness.ports.conversation.submits[0]!.message).toBe("TUI-TYPED-MSG")
    // The composer cleared on submit.
    expect(host.getDraft()).toBe("")
  })

  test("submit with an empty draft does nothing; new-session opens a pending tab", async () => {
    expect(host.keymap.dispatchByKeymap("return")).toBe(false)
    expect(harness.ports.conversation.submits.length).toBe(0)

    expect(host.keymap.dispatchByKeymap("ctrl+n")).toBe(true)
    await setup.waitForFrame((frame) => frame.includes("comuki"))
    const state = harness.kernel.snapshot().state
    expect(state.sessions.length).toBe(1)
    expect(state.sessions[0]!.identity.kind).toBe("pending")
  })
})
