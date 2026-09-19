/**
 * Inline approval card — the production host's card, driven end to
 * end through the kernel with fake ports (issue #73).
 *
 * The card appears when the active session's turn is
 * awaiting-approval and the turn result carried a pendingPlan; every
 * field the plan payload carries renders into the captured frame; y/n
 * (through the registered approval layer) dispatch the right kernel
 * intent; the authoritative decision result replaces the card.
 */

import { beforeEach, afterEach, describe, expect, test } from "bun:test"
import { createTestRenderer } from "@opentui/core/testing"
import { createClientKernel, type ClientKernel } from "../kernel"
import { fakeFeed, fakePorts } from "../kernel/fakes"
import { createTuiHost, type TuiHost } from "./host"

const PLAN = {
  intent: "TUI-INTENT re-run integration suite",
  scope: "tests/Unit.Identity.Oidc.*",
  risk: "medium",
  planSteps: [
    "TUI-APPROVAL-STEP-1 snapshot queue depth",
    "TUI-APPROVAL-STEP-2 dispatch 4 claimers",
    "TUI-APPROVAL-STEP-3 abort on stall > 60s",
  ],
  diff: "+ tests/Unit.Identity.Oidc* --ff\n- tests/Unit.Kafka* --ff",
}

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

describe("tui host — inline approval card over the kernel", () => {
  let setup: Awaited<ReturnType<typeof createTestRenderer>>
  let kernel: ClientKernel
  let conversation: ReturnType<typeof fakePorts>["conversation"]
  let approval: ReturnType<typeof fakePorts>["approval"]
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
    approval = ports.approval
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

  async function submitAskingApproval(): Promise<void> {
    host.setDraft("TUI-APPROVAL-ASK")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)
    conversation.submits[0]!.resolve({
      messages: [
        {
          id: "m-plan",
          role: "assistant",
          content: "TUI-PLAN-PREVIEW",
          createdAtUnixMs: 0,
        },
      ],
      awaitingApproval: true,
      pendingPlan: PLAN,
    })
  }

  test("card appears with every field; approve dispatches the intent and clears on result", async () => {
    await submitAskingApproval()

    await setup.waitForFrame((frame) => frame.includes("intent:"))
    const frame = setup.captureCharFrame()

    // Chrome stays visible alongside the card.
    expect(frame).toContain("comuki · opentui (core)")
    expect(frame).toContain("intent: TUI-INTENT re-run integration suite")
    expect(frame).toContain("scope: tests/Unit.Identity.Oidc.*")
    expect(frame).toContain("risk: medium")
    expect(frame).toContain("plan:")
    for (const step of PLAN.planSteps) {
      expect(frame).toContain(step)
    }
    expect(frame).toContain("diff:")
    for (const line of PLAN.diff.split("\n")) {
      expect(frame).toContain(line)
    }
    expect(frame).toContain("decide:")
    expect(frame).toContain("approve")
    expect(frame).toContain("reject")

    // With no card the command is inert — assert the gate BEFORE the
    // card would be a different test; here the card IS up.
    expect(host.keymap.dispatch("approve")).toBe(true)
    await waitUntil(() => approval.decisions.length > 0)
    const decision = approval.decisions[0]!
    expect(decision.approved).toBe(true)

    // The authoritative decision result replaces the card.
    decision.resolve({
      messages: [
        {
          id: "m-post",
          role: "assistant",
          content: "TUI-POST-APPROVAL",
          createdAtUnixMs: 0,
        },
      ],
      awaitingApproval: false,
    })
    await setup.waitForFrame((frame) => frame.includes("comuki › TUI-POST-APPROVAL"))
    expect(setup.captureCharFrame()).not.toContain("intent:")
  })

  test("reject dispatches approved=false", async () => {
    await submitAskingApproval()
    await setup.waitForFrame((frame) => frame.includes("intent:"))

    expect(host.keymap.dispatch("reject")).toBe(true)
    await waitUntil(() => approval.decisions.length > 0)
    expect(approval.decisions[0]!.approved).toBe(false)
  })

  test("approve/reject are inert while no card is up", async () => {
    expect(host.keymap.dispatch("approve")).toBe(false)
    expect(host.keymap.dispatch("reject")).toBe(false)
    expect(approval.decisions.length).toBe(0)
  })

  test("the platform wire plan shape (nodes) renders its steps", async () => {
    host.setDraft("TUI-NODES-ASK")
    expect(host.keymap.dispatchByKeymap("return")).toBe(true)
    await waitUntil(() => conversation.submits.length > 0)
    conversation.submits[0]!.resolve({
      messages: [],
      awaitingApproval: true,
      pendingPlan: {
        estimateMinutes: 12,
        nodes: [
          { key: "n1", brief: "TUI-NODE-BRIEF-ONE", profileKey: "impl" },
          { key: "n2", brief: "TUI-NODE-BRIEF-TWO", profileKey: "test" },
        ],
        edges: [],
      },
    })
    await setup.waitForFrame((frame) => frame.includes("TUI-NODE-BRIEF-ONE"))
    const frame = setup.captureCharFrame()
    expect(frame).toContain("TUI-NODE-BRIEF-TWO")
    expect(frame).toContain("~12m")
  })
})
