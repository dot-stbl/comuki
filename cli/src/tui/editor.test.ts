/**
 * $EDITOR seam (issue #75) — ctrl+e routes the draft through the
 * TerminalLifecycle seam: suspend → external editor → the returned
 * text adopts as the draft → restore exactly once. Memory-mode tests
 * inject a fake lifecycle + fake editor; no real process is spawned.
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import {
  createTuiHost,
  type TerminalLifecycle,
} from "./host"

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

describe("tui host — external editor through the lifecycle seam", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let kernel: ClientKernel
  let endFeed: () => void

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
    kernel = createClientKernel({ ports: ports.ports, feed: feed.port })
    kernel.start()
    // Issue #77 — bring the hub online.
    feed.push({ kind: "connection", event: "started" })
  })

  afterEach(async () => {
    kernel.stop()
    endFeed()
  })

  test("suspend → editor → adopt text → resume, exactly once each", async () => {
    const order: string[] = []
    const lifecycle: TerminalLifecycle = {
      suspend() {
        order.push("suspend")
      },
      resume() {
        order.push("resume")
      },
    }
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
      terminalLifecycle: lifecycle,
      externalEditor: async (draft) => {
        order.push(`editor:${draft}`)
        return `${draft} + edited in editor`
      },
    })
    await setup.waitForVisualIdle()

    host.setDraft("original draft")
    expect(host.keymap.dispatch("open-editor")).toBe(true)
    await waitUntil(() => host.getDraft() === "original draft + edited in editor")

    expect(order).toEqual([
      "suspend",
      "editor:original draft",
      "resume",
    ])
    await host.destroy()
  })

  test("a throwing editor keeps the draft and still resumes once", async () => {
    const order: string[] = []
    const lifecycle: TerminalLifecycle = {
      suspend() {
        order.push("suspend")
      },
      resume() {
        order.push("resume")
      },
    }
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
      terminalLifecycle: lifecycle,
      externalEditor: async () => {
        throw new Error("editor exploded")
      },
    })
    await setup.waitForVisualIdle()

    host.setDraft("keep me")
    expect(host.keymap.dispatch("open-editor")).toBe(true)
    await waitUntil(() => order.includes("resume"))
    // Give the async seam a beat to settle before asserting.
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(host.getDraft()).toBe("keep me")
    expect(order).toEqual(["suspend", "resume"])
    await host.destroy()
  })

  test("without an injected editor (memory mode) the command is inert", async () => {
    const lifecycle: TerminalLifecycle = {
      suspend() {
        throw new Error("must not suspend")
      },
      resume() {
        throw new Error("must not resume")
      },
    }
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
      terminalLifecycle: lifecycle,
    })
    await setup.waitForVisualIdle()

    host.setDraft("untouched")
    expect(host.keymap.dispatch("open-editor")).toBe(false)
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(host.getDraft()).toBe("untouched")
    await host.destroy()
  })

  test("the ctrl+e chord reaches open-editor through the real key path", async () => {
    const order: string[] = []
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
      terminalLifecycle: {
        suspend() {
          order.push("suspend")
        },
        resume() {
          order.push("resume")
        },
      },
      externalEditor: async (draft) => `${draft}!`,
    })
    await setup.waitForVisualIdle()

    host.setDraft("chord")
    setup.mockInput.pressKey("e", { ctrl: true })
    await waitUntil(() => host.getDraft() === "chord!")
    expect(order).toEqual(["suspend", "resume"])
    await host.destroy()
  })
})
