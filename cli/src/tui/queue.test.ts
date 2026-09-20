/**
 * Follow-up queue (issue #75) — the kernel's per-session queue made
 * visible in the composer chrome, drained automatically when the
 * in-flight turn completes, retained across a /stop cancel (mirrors
 * the Ink host: cancel stops the turn, not the queue).
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

describe("tui host — follow-up queue", () => {
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

  test("submitting while a turn thinks queues visibly; completion drains it", async () => {
    host.setDraft("queue first")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)

    // A thinking turn owns the wire — the second message queues.
    host.setDraft("queue second")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await setup.waitForFrame((frame) => frame.includes("queued:"))
    const queuedFrame = setup.captureCharFrame()
    expect(queuedFrame).toContain("queue second")
    // The echo of the queued message is already in the transcript.
    expect(queuedFrame).toContain("you › queue second")
    expect(conversation.submits.length).toBe(1)

    // Completing the in-flight turn drains the queue automatically
    // (one submit per completion, in order). The indicator clears the
    // moment the drained turn LEAVES the queue (starts thinking).
    conversation.submits[0]!.resolve({
      messages: [
        {
          id: "m-1",
          role: "assistant",
          content: "queue first answer",
          createdAtUnixMs: 0,
        },
      ],
      awaitingApproval: false,
    })
    await waitUntil(() => conversation.submits.length > 1)
    expect(conversation.submits[1]!.message).toBe("queue second")
    await setup.waitForFrame(
      (frame) => frame.includes("queue first answer") && !frame.includes("queued:")
    )
    conversation.submits[1]!.resolve({
      messages: [],
      awaitingApproval: false,
    })
  })

  test("cancel keeps the queue — only the in-flight turn stops", async () => {
    host.setDraft("cancel me")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)

    host.setDraft("stay queued")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await setup.waitForFrame((frame) => frame.includes("queued:"))

    // /stop — the escape chord cancels the thinking turn.
    expect(host.keymap.dispatchByKeymap("escape")).toBe(true)
    await waitUntil(() => {
      const session = kernel.snapshot().state.sessions[0]
      return session !== undefined && session.turn.kind === "idle"
    })

    // The queue indicator survived the cancel (matches the Ink host).
    await setup.waitForFrame((frame) => frame.includes("queued:"))
    expect(setup.captureCharFrame()).toContain("stay queued")
  })
})
