import { describe, expect, it } from "bun:test"
import { reduceHarness } from "../harness/reducer"
import {
  initialHarnessState,
  pendingSessionId,
  sessionId,
  turnRequestId,
  type HarnessState,
} from "../harness/state"
import {
  emptyMirrorMemory,
  mirrorKernelSnapshot,
  type MirrorMemory,
} from "./kernel-mirror"
import { PENDING_PREFIX, type SessionsState } from "./session-state"

const pendingId = pendingSessionId("local-1-0")
const remoteId = sessionId("server-1")

function emptyTabs(): SessionsState {
  return { sessions: [], activeIndex: -1 }
}

function kernelWithPending(): HarnessState {
  return reduceHarness(initialHarnessState(), {
    type: "pending-session-opened",
    pendingSessionId: pendingId,
    projectId: null,
    createdAtUnixMs: 10,
  }).state
}

function kernelAdopted(): HarnessState {
  return reduceHarness(kernelWithPending(), {
    type: "remote-session-adopted",
    pendingSessionId: pendingId,
    sessionId: remoteId,
    projectId: null,
    title: "First task",
  }).state
}

describe("mirrorKernelSnapshot", () => {
  it("creates a tab for a restored remote session, unhydrated", () => {
    const workspace = kernelAdopted()
    const result = mirrorKernelSnapshot(emptyTabs(), workspace, emptyMirrorMemory())

    expect(result.tabs.sessions).toHaveLength(1)
    expect(result.tabs.sessions[0]?.id).toBe(remoteId)
    expect(result.tabs.sessions[0]?.name).toBe("First task")
    expect(result.tabs.sessions[0]?.hydrated).toBeFalse()
    expect(result.tabs.activeIndex).toBe(0)
  })

  it("mirrors a pending tab and rebinds its id on adoption", () => {
    const pending = mirrorKernelSnapshot(emptyTabs(), kernelWithPending(), emptyMirrorMemory())
    expect(pending.tabs.sessions[0]?.id).toBe(pendingId)
    expect(pending.tabs.sessions[0]?.id.startsWith(PENDING_PREFIX)).toBeTrue()

    const adopted = mirrorKernelSnapshot(pending.tabs, kernelAdopted(), pending.memory)
    expect(adopted.tabs.sessions).toHaveLength(1)
    expect(adopted.tabs.sessions[0]?.id).toBe(remoteId)
    expect(adopted.tabs.sessions[0]?.name).toBe("First task")
    // After adoption the binding is the identity: the legacy tab id
    // itself became the server id.
    expect(adopted.memory.bindings.get(remoteId)).toBe(remoteId)
  })

  it("appends transcript blocks exactly once across duplicate snapshots", () => {
    let kernel = kernelAdopted()
    kernel = reduceHarness(kernel, {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      message: "Hello",
      commandId: "cmd-1",
      echoText: "Hello",
    }).state
    kernel = reduceHarness(kernel, {
      type: "turn-completed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      messages: [
        { id: "m1", role: "assistant", content: "Answer", createdAtUnixMs: 5 },
      ],
      awaitingApproval: false,
    }).state

    const first = mirrorKernelSnapshot(emptyTabs(), kernel, emptyMirrorMemory())
    // Replayed snapshot (no state change) must not re-append anything.
    const second = mirrorKernelSnapshot(first.tabs, kernel, first.memory)

    expect(first.tabs.sessions[0]?.blocks.map((block) => block.kind)).toEqual([
      "message",
      "message",
    ])
    expect(second.tabs).toBe(first.tabs)
  })

  it("reports completion + stopped once per turn transition", () => {
    let kernel = kernelAdopted()
    const submitted = reduceHarness(kernel, {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      message: "Hello",
      commandId: "cmd-1",
      echoText: "Hello",
    }).state
    const thinking = reduceHarness(submitted, {
      type: "thinking-started",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
    }).state
    const failed = reduceHarness(thinking, {
      type: "turn-failed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      error: { kind: "aborted", code: "operation.aborted", message: "x", retryable: false },
    }).state
    kernel = thinking

    const seeded = mirrorKernelSnapshot(emptyTabs(), kernelAdopted(), emptyMirrorMemory())
    let memory: MirrorMemory = seeded.memory
    let tabs = seeded.tabs
    ;({ tabs, memory } = mirrorKernelSnapshot(tabs, submitted, memory))
    ;({ tabs, memory } = mirrorKernelSnapshot(tabs, thinking, memory))
    expect(tabs.sessions[0]?.status).toBe("thinking")

    const done = mirrorKernelSnapshot(tabs, failed, memory)
    expect(done.completedTitles).toEqual(["First task"])
    expect(done.stoppedSessionIds).toEqual([remoteId])
    expect(done.tabs.sessions[0]?.status).toBe("done")
  })

  it("maps awaiting-approval and pendingPlan onto the tab", () => {
    let kernel = kernelAdopted()
    kernel = reduceHarness(kernel, {
      type: "turn-completed",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      messages: [],
      awaitingApproval: true,
      pendingPlan: { nodes: [1] },
    }).state

    const result = mirrorKernelSnapshot(emptyTabs(), kernel, emptyMirrorMemory())
    expect(result.tabs.sessions[0]?.awaitingApproval).toBeTrue()
    expect(result.tabs.sessions[0]?.pendingPlan).toEqual({ nodes: [1] })
  })

  it("mirrors the kernel queue as legacy queued messages", () => {
    let kernel = kernelAdopted()
    const first = reduceHarness(kernel, {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      message: "one",
      commandId: "cmd-1",
      echoText: "one",
    }).state
    const thinking = reduceHarness(first, {
      type: "thinking-started",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
    }).state
    kernel = reduceHarness(thinking, {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-2"),
      message: "two",
      commandId: "cmd-2",
      echoText: "two",
    }).state

    const seeded = mirrorKernelSnapshot(emptyTabs(), kernelAdopted(), emptyMirrorMemory())
    const result = mirrorKernelSnapshot(seeded.tabs, kernel, seeded.memory)
    expect(result.tabs.sessions[0]?.queued).toEqual(["two"])
  })

  it("removes the tab when the kernel session closes, keeps foreign tabs", () => {
    const seeded = mirrorKernelSnapshot(emptyTabs(), kernelAdopted(), emptyMirrorMemory())
    // A legacy-only tab (e.g. a /branch fork) the kernel never saw.
    const withForeign: SessionsState = {
      sessions: [
        ...seeded.tabs.sessions,
        { ...seeded.tabs.sessions[0], id: "fork-1", name: "fork" },
      ],
      activeIndex: 0,
    }

    const closed = reduceHarness(kernelAdopted(), {
      type: "session-closed",
      sessionId: remoteId,
    }).state
    const result = mirrorKernelSnapshot(withForeign, closed, seeded.memory)

    expect(result.tabs.sessions).toHaveLength(1)
    expect(result.tabs.sessions[0]?.id).toBe("fork-1")
  })

  it("never appends a transcript twice after hydration rebuilt blocks", () => {
    const seeded = mirrorKernelSnapshot(emptyTabs(), kernelAdopted(), emptyMirrorMemory())
    // Legacy hydration replaced the transcript wholesale.
    const hydrated: SessionsState = {
      sessions: seeded.tabs.sessions.map((tab) => ({
        ...tab,
        hydrated: true,
        blocks: [
          { kind: "message", key: "h0", message: { id: "h0", role: "user", content: "old", toolName: null, parts: null, meta: null, createdAt: "2026-01-01T00:00:00.000Z" } },
        ],
      })),
      activeIndex: seeded.tabs.activeIndex,
    }

    const kernel = reduceHarness(kernelAdopted(), {
      type: "turn-queued",
      sessionId: remoteId,
      requestId: turnRequestId("cmd-1"),
      message: "Hello",
      commandId: "cmd-1",
      echoText: "Hello",
    }).state

    const result = mirrorKernelSnapshot(hydrated, kernel, seeded.memory)
    // The kernel echo lands after the hydrated block, hydration survives.
    expect(result.tabs.sessions[0]?.blocks).toHaveLength(2)
    expect(result.tabs.sessions[0]?.blocks[1]?.kind).toBe("message")
  })
})
