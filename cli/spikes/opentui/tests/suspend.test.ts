/**
 * Suspend / resume — terminal cleanup boundary.
 *
 * The Core renderer's `suspend()` and `resume()` methods are the
 * public boundary the chat shell uses for:
 *   - opening the user in `$EDITOR` for a composer draft
 *   - shelling out to `git add -p` / `kubectl edit`
 *   - any external process that needs the raw TTY
 *
 * The chat shell wires this through a thin wrapper: `suspendForEdit()`
 * exposes an injectable `TerminalLifecycleHooks` so tests can spy
 * on the order of suspend / editor / onRestore / resume without
 * requiring a real TTY. The seam fires hooks BEFORE the real call
 * (or as a no-op in `memoryMode`) and AFTER it in the `finally` block.
 *
 * Each test uses a single renderer / single shell, observes the
 * order with a spy array, and asserts the canonical sequence.
 */

import { test, expect, describe, beforeEach, afterEach } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

type Call =
  | { kind: "suspend" }
  | { kind: "editor-start" }
  | { kind: "editor-end" }
  | { kind: "onRestore" }
  | { kind: "resume" }

describe("suspend / resume — terminal cleanup boundary", () => {
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

  test("memory mode: onSuspend -> editor -> onRestore -> onResume on success", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    const calls: Call[] = []
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
        hooks: {
          onSuspend: () => calls.push({ kind: "suspend" }),
          onResume: () => calls.push({ kind: "resume" }),
        },
      },
    )

    expect(editorCalled).toBe(1)
    expect(restoreCalled).toBe(1)
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.value).toBe("edited draft text")
      expect(shell.getDraft()).toBe("edited draft text")
    }

    // Lifecycle order: suspend -> editor-start -> editor-end ->
    // onRestore -> resume. In memoryMode the OpenTUI renderer
    // hooks themselves are no-ops, but the observable hooks the
    // spike injects still fire in this exact order.
    expect(calls).toEqual([
      { kind: "suspend" },
      { kind: "editor-start" },
      { kind: "editor-end" },
      { kind: "onRestore" },
      { kind: "resume" },
    ])
  })

  test("memory mode: onSuspend -> editor -> onRestore -> onResume on failure", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    const calls: Call[] = []
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
        hooks: {
          onSuspend: () => calls.push({ kind: "suspend" }),
          onResume: () => calls.push({ kind: "resume" }),
        },
      },
    )

    expect(restoreCalled).toBe(1)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.message).toBe("editor exploded")
    }

    // Even on failure, the order holds: suspend -> editor (start
    // and end) -> onRestore -> resume. The spike guarantees the
    // `finally` runs through these hooks so an editor crash does
    // not leave the terminal in a half-suspended state.
    expect(calls).toEqual([
      { kind: "suspend" },
      { kind: "editor-start" },
      { kind: "editor-end" },
      { kind: "onRestore" },
      { kind: "resume" },
    ])
  })

  test("memory mode is a documented no-op for the renderer-level suspend/resume (hooks still fire)", async () => {
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
        return "ok"
      },
      {
        onRestore: () => {
          restoreCalled += 1
        },
      },
    )
    expect(result.ok).toBe(true)
    expect(editorCalled).toBe(1)
    expect(restoreCalled).toBe(1)
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
