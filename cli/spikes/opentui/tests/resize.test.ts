/**
 * Resize across the issue's required geometry sweep — real Core shell.
 *
 * One `createTestRenderer` instance, one `createChatShell` instance,
 * one draft set BEFORE the first resize. The test then sequentially
 * calls `shell.setSize(160, 50) → (80, 24) → (48, 16)` and after
 * each call asserts the same `CORE-DRAFT-PRESERVED` string is
 * present in the captured frame, alongside the top-bar (chrome) and
 * a recognizable transcript marker.
 *
 * The audit's correction is the explicit "one renderer / one shell /
 * one setDraft" structure. The same `setup` and `shell` flow through
 * every assertion. The draft is set once in `beforeEach` and must
 * survive every subsequent `setSize` call without re-injection.
 *
 * 48x16 falls into the compact layout (chrome shortening, composer
 * collapsing). The captured frame must still contain all three
 * semantic regions; the test fails loudly if not.
 */

import { test, expect, describe, beforeEach, afterEach } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

const DRAFT = "CORE-DRAFT-PRESERVED"
const GEOMETRIES = [
  [160, 50],
  [80, 24],
  [48, 16],
] as const

describe("core chat shell — real resize sweep (single renderer, single shell, single draft)", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let shell: Awaited<ReturnType<typeof createChatShell>>

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 160,
      height: 50,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    shell = await createChatShell(
      { width: 160, height: 50, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
    await setup.waitForVisualIdle()
    // One setDraft for the whole sweep — no per-resize re-injection.
    shell.setDraft(DRAFT)
    await setup.waitForVisualIdle()
  })

  afterEach(async () => {
    await shell.destroy()
    try {
      setup.renderer.destroy()
    } catch {
      // idempotent
    }
  })

  test("draft survives the full 160x50 → 80x24 → 48x16 sweep", async () => {
    for (const [w, h] of GEOMETRIES) {
      await shell.setSize(w, h)
      await setup.waitForVisualIdle()

      const frame = setup.captureCharFrame()

      // chrome (top bar) — wording may shorten at compact 48x16.
      const topBarPresent =
        frame.includes("comuki · opentui-spike") ||
        frame.includes("comuki·opentui-spike")
      expect(topBarPresent).toBe(true)

      // transcript marker — the sticky-bottom viewport always shows
      // the fixture's latest entries; we look for any assistant line.
      expect(frame).toMatch(/Answer #99\d:/)

      // composer — the same draft set ONCE before the first resize.
      // The compact mode may keep the draft visible even with
      // 1-row composer; we look for the literal string the host
      // typed.
      expect(frame).toContain(DRAFT)
    }
  })
})
