/**
 * Suspend / resume — terminal lifecycle contract.
 *
 * The Core renderer's `suspend()` and `resume()` methods are the
 * public boundary the chat shell uses for:
 *   - opening the user in `$EDITOR` for a composer draft
 *   - shelling out to `git add -p` / `kubectl edit`
 *   - any external process that needs the raw TTY
 *
 * `createChatShell` resolves a `TerminalLifecycle` per:
 *   1. `internals.terminalLifecycle` if supplied — the injected seam.
 *   2. A no-op object when `memoryMode === true`.
 *   3. An adapter over `renderer.suspend()` / `renderer.resume()`.
 *
 * `suspendForEdit` is the public consumer: it calls
 * `lifecycle.suspend()` first, then the editor, then `onRestore`,
 * then `lifecycle.resume()` in the `finally` block. If `suspend()`
 * throws, the editor / `onRestore` / `resume` do not run.
 *
 * Each test injects a spy lifecycle through `internals.terminalLifecycle`,
 * records the call sequence into a shared array, and asserts the
 * canonical ordering (suspend → editor-start → editor-end → onRestore
 * → resume). One test injects a lifecycle whose `suspend()` throws
 * to verify the editor / onRestore / resume paths do not run in that
 * case. One test exercises the no-injection + memoryMode no-op path
 * so we know the fallback works without a real TTY.
 *
 * Scope: the lifecycle contract is tested end-to-end with the spike's
 * test renderer; a real TTY is **not** exercised by these tests.
 */

import { test, expect, describe, beforeEach, afterEach } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell, type TerminalLifecycle } from "../src/core/chat-shell.js"

type Call =
  | { kind: "suspend" }
  | { kind: "editor-start" }
  | { kind: "editor-end" }
  | { kind: "onRestore" }
  | { kind: "resume" }

/**
 * Spy lifecycle used by the lifecycle-contract tests. Each call records
 * `{ kind: "suspend" | "resume" }` into the shared `calls` array. With
 * `throwOnSuspend`, `suspend()` records the call first and then throws —
 * the test asserts the editor / onRestore / resume paths do not run
 * after the throw.
 *
 * The returned `lifecycle` is what `tests/suspend.test.ts` injects via
 * `ChatShellInternals.terminalLifecycle`. Tests assert the canonical
 * call sequence and the exact suspend/resume counts. **Lifecycle
 * contract tested; real TTY unverified.**
 */
function makeSpyLifecycle(opts?: {
  throwOnSuspend?: boolean
  suspendMessage?: string
}): { readonly lifecycle: TerminalLifecycle; readonly calls: Call[] } {
  const calls: Call[] = []
  return {
    calls,
    lifecycle: {
      suspend: () => {
        calls.push({ kind: "suspend" })
        if (opts?.throwOnSuspend) {
          throw new Error(opts.suspendMessage ?? "lifecycle.suspend failed")
        }
      },
      resume: () => {
        calls.push({ kind: "resume" })
      },
    },
  }
}

describe("suspend / resume — terminal lifecycle contract", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let shell: Awaited<ReturnType<typeof createChatShell>> | null = null

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 80,
      height: 24,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    shell = null
  })

  afterEach(async () => {
    if (shell) {
      await shell.destroy()
      shell = null
    }
    try {
      setup.renderer.destroy()
    } catch {
      // idempotent
    }
  })

  test("injected spy: suspend → editor → onRestore → resume on success (exactly one each)", async () => {
    // Lifecycle contract tested; real TTY unverified.
    const { calls, lifecycle } = makeSpyLifecycle()
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      {
        renderer: setup.renderer,
        memoryMode: true,
        terminalLifecycle: lifecycle,
      }
    )
    await setup.waitForVisualIdle()

    let editorCalled = 0
    let restoreCalled = 0

    const result = await shell.suspendForEdit(
      async () => {
        editorCalled += 1
        calls.push({ kind: "editor-start" })
        // Yield a microtask so the order assertion is meaningful.
        await Promise.resolve()
        calls.push({ kind: "editor-end" })
        return "edited draft text"
      },
      {
        onRestore: () => {
          restoreCalled += 1
          calls.push({ kind: "onRestore" })
        },
      }
    )

    // Lifecycle contract: exactly one suspend and one resume.
    expect(calls.filter((c) => c.kind === "suspend")).toHaveLength(1)
    expect(calls.filter((c) => c.kind === "resume")).toHaveLength(1)
    expect(editorCalled).toBe(1)
    expect(restoreCalled).toBe(1)

    expect(result.ok).toBe(true)
    if (!result.ok) {
      throw new Error(`expected ok, got ${JSON.stringify(result)}`)
    }
    expect(result.value).toBe("edited draft text")
    expect(shell.getDraft()).toBe("edited draft text")

    // Canonical order: suspend → editor-start → editor-end → onRestore → resume.
    expect(calls).toEqual([
      { kind: "suspend" },
      { kind: "editor-start" },
      { kind: "editor-end" },
      { kind: "onRestore" },
      { kind: "resume" },
    ])
  })

  test("injected spy: suspend → editor → onRestore → resume when editor throws (exactly one each)", async () => {
    // Lifecycle contract tested; real TTY unverified.
    const { calls, lifecycle } = makeSpyLifecycle()
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      {
        renderer: setup.renderer,
        memoryMode: true,
        terminalLifecycle: lifecycle,
      }
    )
    await setup.waitForVisualIdle()

    let restoreCalled = 0

    const result = await shell.suspendForEdit(
      async () => {
        calls.push({ kind: "editor-start" })
        await Promise.resolve()
        calls.push({ kind: "editor-end" })
        throw new Error("editor exploded")
      },
      {
        onRestore: () => {
          restoreCalled += 1
          calls.push({ kind: "onRestore" })
        },
      }
    )

    // Lifecycle contract: exactly one suspend and one resume, even when
    // the editor body throws — the `finally` block runs `resume()`
    // because `suspend()` had succeeded.
    expect(calls.filter((c) => c.kind === "suspend")).toHaveLength(1)
    expect(calls.filter((c) => c.kind === "resume")).toHaveLength(1)
    expect(restoreCalled).toBe(1)

    expect(result.ok).toBe(false)
    if (result.ok) {
      throw new Error(`expected error, got ${JSON.stringify(result)}`)
    }
    expect(result.error.message).toBe("editor exploded")

    // Canonical order holds on failure: suspend → editor → onRestore → resume.
    expect(calls).toEqual([
      { kind: "suspend" },
      { kind: "editor-start" },
      { kind: "editor-end" },
      { kind: "onRestore" },
      { kind: "resume" },
    ])
  })

  test("injected spy: lifecycle.suspend throws — editor / onRestore / resume do not run", async () => {
    // Lifecycle contract tested; real TTY unverified.
    const { calls, lifecycle } = makeSpyLifecycle({
      throwOnSuspend: true,
      suspendMessage: "suspend failed",
    })
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      {
        renderer: setup.renderer,
        memoryMode: true,
        terminalLifecycle: lifecycle,
      }
    )
    await setup.waitForVisualIdle()

    let editorCalled = 0
    let restoreCalled = 0

    const result = await shell.suspendForEdit(
      async () => {
        editorCalled += 1
        calls.push({ kind: "editor-start" })
        await Promise.resolve()
        calls.push({ kind: "editor-end" })
        return "should not be returned"
      },
      {
        onRestore: () => {
          restoreCalled += 1
          calls.push({ kind: "onRestore" })
        },
      }
    )

    // When suspend throws, the editor body never runs and `onRestore`
    // never fires. `resume` must also not run — calling it on a never-
    // suspended terminal would leave the renderer in a half-restored
    // state.
    expect(editorCalled).toBe(0)
    expect(restoreCalled).toBe(0)
    expect(calls.filter((c) => c.kind === "editor-start")).toHaveLength(0)
    expect(calls.filter((c) => c.kind === "editor-end")).toHaveLength(0)
    expect(calls.filter((c) => c.kind === "onRestore")).toHaveLength(0)
    expect(calls.filter((c) => c.kind === "resume")).toHaveLength(0)
    // Suspend itself is recorded — the throw happens after the push.
    expect(calls.filter((c) => c.kind === "suspend")).toHaveLength(1)

    expect(result.ok).toBe(false)
    if (result.ok) {
      throw new Error(`expected error, got ${JSON.stringify(result)}`)
    }
    expect(result.error.message).toBe("suspend failed")
  })

  test("memoryMode without injection: no-op lifecycle — editor runs, no spy calls", async () => {
    // Lifecycle contract tested; real TTY unverified.
    // No `terminalLifecycle` passed in internals; with `memoryMode=true`
    // the shell falls through to its built-in no-op lifecycle. The
    // point of this test is to verify the no-op fallback does not
    // crash and does not require any real terminal interaction.
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    let editorCalled = 0
    let restoreCalled = 0

    const result = await shell.suspendForEdit(
      async () => {
        editorCalled += 1
        return "noop path"
      },
      {
        onRestore: () => {
          restoreCalled += 1
        },
      }
    )

    expect(result.ok).toBe(true)
    if (!result.ok) {
      throw new Error(`expected ok, got ${JSON.stringify(result)}`)
    }
    expect(result.value).toBe("noop path")
    expect(editorCalled).toBe(1)
    expect(restoreCalled).toBe(1)
    expect(shell.getDraft()).toBe("noop path")
  })

  test("after cleanup the next frame still renders the chat surface", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    await shell.suspendForEdit(async () => "after suspend")
    await setup.waitForVisualIdle()

    const frame = setup.captureCharFrame()
    expect(frame).toContain("comuki · opentui-spike (core) · focus-mode")
    expect(frame).toMatch(/Answer #99\d:/)
  })
})