/**
 * Exit lifecycle — the clean-exit contract of the production host:
 *
 *   exit command → unsubscribe → keymap teardown → kernel.stop() →
 *   await kernel.whenIdle() (in-flight effects settle through the
 *   abort path) → renderer.destroy() (terminal restored) — in that
 *   ORDER, exactly once, idempotently.
 *
 * The order is observed through delegating proxies over the kernel
 * and the renderer; a pending submit is left in flight so the test
 * proves whenIdle still resolves after stop() (the abort path).
 */

import { beforeEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import type { CliRenderer } from "@opentui/core"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import type { PendingSubmit } from "../kernel/fakes"
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

describe("tui host — exit lifecycle ordering", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let kernel: ClientKernel
  let submits: readonly PendingSubmit[]
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
    submits = ports.conversation.submits
    kernel = createClientKernel({ ports: ports.ports, feed: feed.port })
    kernel.start()
  })

  test("exit: kernel.stop → whenIdle → renderer.destroy, exactly once, idempotent", async () => {
    const sequence: string[] = []

    const observedKernel: ClientKernel = new Proxy(kernel, {
      get(target, prop) {
        if (prop === "stop") {
          return () => {
            sequence.push("kernel-stop")
            target.stop()
          }
        }
        if (prop === "whenIdle") {
          return () =>
            target.whenIdle().then(() => {
              sequence.push("whenIdle-resolved")
            })
        }
        const value = Reflect.get(target, prop, target)
        return typeof value === "function" ? value.bind(target) : value
      },
    })

    let rendererDestroys = 0
    const observedRenderer: CliRenderer = new Proxy(setup.renderer, {
      get(target, prop) {
        if (prop === "destroy") {
          return () => {
            rendererDestroys += 1
            sequence.push("renderer-destroy")
            target.destroy()
          }
        }
        const value = Reflect.get(target, prop, target)
        return typeof value === "function" ? value.bind(target) : value
      },
    }) as CliRenderer

    let exited = false
    const host: TuiHost = await createTuiHost(observedKernel, {
      renderer: observedRenderer,
      width: 80,
      height: 24,
      memoryMode: true,
      onExit: () => {
        exited = true
      },
    })
    await setup.waitForVisualIdle()

    // Leave a submit in flight on the ADOPTED (remote) session — its
    // wire effect carries the command id, so kernel.stop()'s abort
    // settles it and whenIdle resolves (the production adapter's
    // abort path; a pending→adopted first submit carries no command
    // id on the wire, so the fake would leave it parked).
    host.setDraft("TUI-EXIT-FIRST")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => submits.length > 0)
    submits[0]!.resolve({
      messages: [],
      awaitingApproval: false,
    })
    await waitUntil(() => kernel.snapshot().state.sessions.some(
      (session) =>
        session.identity.kind === "remote" && session.turn.kind === "idle"
    ))

    host.setDraft("TUI-EXIT-MSG")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => submits.length > 1)

    // The exit command fires the sequence; a second close() call
    // awaits the same promise instead of double-running it.
    expect(host.keymap.dispatch("exit")).toBe(true)
    await host.close()
    await host.close()

    expect(host.isClosed()).toBe(true)
    expect(host.isKernelStopped()).toBe(true)
    expect(exited).toBe(true)
    expect(rendererDestroys).toBe(1)

    const stopIndex = sequence.indexOf("kernel-stop")
    const idleIndex = sequence.indexOf("whenIdle-resolved")
    const destroyIndex = sequence.indexOf("renderer-destroy")
    expect(stopIndex).toBeGreaterThanOrEqual(0)
    expect(idleIndex).toBeGreaterThan(stopIndex)
    expect(destroyIndex).toBeGreaterThan(idleIndex)
    expect(sequence.filter((entry) => entry === "kernel-stop").length).toBe(1)

    kernel.stop()
    endFeed()
  })

  test("stopped kernel ignores further dispatch", async () => {
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
    })
    await setup.waitForVisualIdle()
    await host.close()

    const revision = kernel.snapshot().revision
    kernel.dispatch({ kind: "open-session" })
    expect(kernel.snapshot().revision).toBe(revision)

    kernel.stop()
  })

  test("ctrl+c alias is the exit command (clean exit chord)", async () => {
    const host = await createTuiHost(kernel, {
      renderer: setup.renderer,
      width: 80,
      height: 24,
      memoryMode: true,
    })
    await setup.waitForVisualIdle()

    expect(host.keymap.dispatchByKeymap("ctrl+c")).toBe(true)
    await host.close()
    expect(host.isClosed()).toBe(true)
    expect(host.isKernelStopped()).toBe(true)
    kernel.stop()
    endFeed()
  })
})
