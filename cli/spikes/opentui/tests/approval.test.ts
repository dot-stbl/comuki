/**
 * Inline approval — real Core proof.
 *
 * The issue's architectural question: "Render a medium-risk inline
 * approval with plan and one-file diff." This test asserts that
 * a real OpenTUI Core chat shell renders the approval card into
 * its captured frame, alongside the top bar. Each section has a
 * distinctive prefix so the assertion is exact, not approximate.
 *
 * The card sits at the bottom of the column-flex shell; it
 * replaces most of the viewport height while pending. Approve
 * and reject are real `SelectRenderable` actions rendered as
 * options inside the card.
 */

import { test, expect, describe, beforeEach, afterEach } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"
import type { ApprovalPlan } from "../src/commands/registry.js"

const PLAN: ApprovalPlan = {
  intent: "CORE-APPROVAL-INTENT re-run integration suite",
  scope: "tests/Unit.Identity.Oidc.*",
  risk: "medium",
  planSteps: [
    "CORE-APPROVAL-STEP-1 snapshot current merge-queue depth",
    "CORE-APPROVAL-STEP-2 dispatch 4 claimers to Oidc test files",
    "CORE-APPROVAL-STEP-3 abort + requeue if any claimer stalls > 60s",
  ],
  diff: "+ tests/Unit.Identity.Oidc* --ff\n- tests/Unit.Kafka* --ff",
}

describe("core chat shell — inline approval is a real renderable in captureCharFrame", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let shell: Awaited<ReturnType<typeof createChatShell>>

  beforeEach(async () => {
    setup = await createTestRenderer({
      width: 80,
      height: 24,
      kittyKeyboard: false,
      otherModifiersMode: true,
    })
    shell = await createChatShell(
      { width: 80, height: 24, focusMode: true },
      { renderer: setup.renderer, memoryMode: true }
    )
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

  test("approval renders intent / scope / risk / plan / step / diff / approve / reject in the captured frame", async () => {
    shell.setApproval(PLAN)
    await setup.waitForVisualIdle()

    const frame = setup.captureCharFrame()

    // Chrome is still visible alongside the approval — the
    // approval replaces most of the viewport height, not the
    // whole shell.
    expect(frame).toContain("comuki · opentui-spike (core)")

    // Each section has a distinctive prefix the host typed into
    // `ApprovalPlan`. The same prefixes appear in `chat-shell.ts`
    // (`INTENT_PREFIX`, `SCOPE_PREFIX`, …). The captured frame
    // MUST contain them all — that is the issue's "intent, scope,
    // risk, plan, diff" requirement.
    expect(frame).toContain("APPROVAL-INTENT: CORE-APPROVAL-INTENT re-run integration suite")
    expect(frame).toContain("APPROVAL-SCOPE: tests/Unit.Identity.Oidc.*")
    expect(frame).toContain("APPROVAL-RISK: medium")
    expect(frame).toContain("APPROVAL-PLAN:")
    expect(frame).toContain("APPROVAL-STEP-1 snapshot current merge-queue depth")
    expect(frame).toContain("APPROVAL-DIFF:")
    expect(frame).toContain("+ tests/Unit.Identity.Oidc* --ff")
    expect(frame).toContain("- tests/Unit.Kafka* --ff")

    // Approve and reject are real `SelectRenderable` options.
    // The `description` of each option carries a distinct prefix
    // that the renderer renders into the captured frame.
    expect(frame).toContain("APPROVAL-ACTION-APPROVE")
    expect(frame).toContain("APPROVAL-ACTION-REJECT")
  })

  test("clearing the approval drops the card from the frame and restores the viewport tail", async () => {
    shell.setApproval(PLAN)
    await setup.waitForVisualIdle()

    shell.setApproval(null)
    await setup.waitForVisualIdle()

    const frame = setup.captureCharFrame()
    expect(frame).not.toContain("APPROVAL-INTENT:")
    // Once the overlay is gone the sticky-bottom viewport restores
    // its tail, which is always populated by the fixture.
    expect(frame).toMatch(/Answer #99\d:/)
  })
})
