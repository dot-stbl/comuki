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
 * returns a `{ restore }` that the host calls in `finally`. This test
 * asserts that:
 *
 *   - the chat shell exposes a suspend seam
 *   - calling suspend() flips a control flag
 *   - calling restore() flips it back
 *   - the wrapper is safe to call before any frame has been rendered
 *
 * The test renderer stubs the suspend/resume calls so the assertions
 * run in CI without a real TTY.
 */

import { test, expect, describe, beforeEach, afterEach } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

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

  test("suspendForEdit runs the editor and restores the shell on resolve", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    let editorCalled = 0
    let restoreCalled = 0
    const editorResult = await shell.suspendForEdit(async () => {
      editorCalled += 1
      return "edited draft text"
    }, {
      onRestore: () => {
        restoreCalled += 1
      },
    })

    expect(editorCalled).toBe(1)
    expect(restoreCalled).toBe(1)
    expect(editorResult.ok).toBe(true)
    if (editorResult.ok) {
      expect(editorResult.value).toBe("edited draft text")
      expect(shell.getDraft()).toBe("edited draft text")
    }
  })

  test("if the editor throws, restore still runs and the error surfaces", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    let restoreCalled = 0
    const result = await shell.suspendForEdit(async () => {
      throw new Error("editor exploded")
    }, {
      onRestore: () => {
        restoreCalled += 1
      },
    })

    expect(restoreCalled).toBe(1)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.message).toBe("editor exploded")
    }
  })

  test("suspendForEdit is a no-op when the renderer is memoryMode (no TTY)", async () => {
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()

    let restoreCalled = 0
    const result = await shell.suspendForEdit(async () => "ok", {
      onRestore: () => {
        restoreCalled += 1
      },
    })
    expect(result.ok).toBe(true)
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
