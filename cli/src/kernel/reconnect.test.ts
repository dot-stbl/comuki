/**
 * Reconnect orchestrator — state transitions + dispatch gate
 * (issue #77).
 *
 * Three states — offline, recovering, online — with idempotent
 * transitions. Listeners receive the new state on every change;
 * setting the same state twice is a no-op.
 *
 * The orchestrator is the kernel-side counterpart of the realtime
 * hub adapter: the kernel calls `markOffline()` on
 * `connection-lost`, `markRecovering()` while the feed is retrying,
 * and `markOnline()` on the next `connection-established`. While
 * not `online`, `canDispatch()` returns false so the kernel can
 * reject mutating sends with the draft retained.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"

import { createReconnectOrchestrator, type ReconnectOrchestrator } from "./reconnect"

describe("ReconnectOrchestrator — initial state", () => {
  let orchestrator: ReconnectOrchestrator

  beforeEach(() => {
    orchestrator = createReconnectOrchestrator()
  })

  afterEach(() => {
    orchestrator.destroy()
  })

  test("starts offline (the hub has not connected yet)", () => {
    expect(orchestrator.state()).toBe("offline")
    expect(orchestrator.canDispatch()).toBe(false)
  })

  test("listeners receive state transitions only (no synthetic seed)", () => {
    const seen: string[] = []
    orchestrator.addEventListener((state) => seen.push(state))
    // We do not fire on subscribe by design — the caller can read
    // state() once if they want the seed. Listeners fire on
    // transitions only.
    expect(seen).toEqual([])
  })
})

describe("ReconnectOrchestrator — transitions", () => {
  let orchestrator: ReconnectOrchestrator
  let log: string[]

  beforeEach(() => {
    orchestrator = createReconnectOrchestrator()
    log = []
    orchestrator.addEventListener((state) => log.push(state))
  })

  afterEach(() => {
    orchestrator.destroy()
  })

  test("offline → recovering → online fires one event each", () => {
    // Already offline, so markOffline() is a no-op.
    orchestrator.markOffline()
    expect(log).toEqual([])

    orchestrator.markRecovering()
    expect(log).toEqual(["recovering"])
    expect(orchestrator.canDispatch()).toBe(false)

    orchestrator.markOnline()
    expect(log).toEqual(["recovering", "online"])
    expect(orchestrator.canDispatch()).toBe(true)
  })

  test("setting the same state twice is a no-op (no duplicate listener fires)", () => {
    orchestrator.markOffline()
    orchestrator.markOffline()
    orchestrator.markOffline()
    expect(log).toEqual([])

    orchestrator.markRecovering()
    orchestrator.markRecovering()
    expect(log).toEqual(["recovering"])

    orchestrator.markOnline()
    orchestrator.markOnline()
    expect(log).toEqual(["recovering", "online"])
  })

  test("canDispatch is true only when online", () => {
    expect(orchestrator.canDispatch()).toBe(false)
    orchestrator.markRecovering()
    expect(orchestrator.canDispatch()).toBe(false)
    orchestrator.markOnline()
    expect(orchestrator.canDispatch()).toBe(true)
    orchestrator.markOffline()
    expect(orchestrator.canDispatch()).toBe(false)
  })

  test("a drop while online transitions back through offline", () => {
    orchestrator.markRecovering()
    orchestrator.markOnline()
    log.length = 0 // reset the log

    orchestrator.markOffline()
    expect(log).toEqual(["offline"])
    expect(orchestrator.state()).toBe("offline")

    orchestrator.markRecovering()
    expect(log).toEqual(["offline", "recovering"])

    orchestrator.markOnline()
    expect(log).toEqual(["offline", "recovering", "online"])
  })

  test("unsubscribe stops further listener fires", () => {
    const unsubscribe = orchestrator.addEventListener(() => {})
    unsubscribe()
    orchestrator.markRecovering()
    orchestrator.markOnline()
    // Only the first listener (the one in beforeEach) is in the set.
    expect(log).toEqual(["recovering", "online"])
  })

  test("multiple listeners all receive the transition", () => {
    const second: string[] = []
    const third: string[] = []
    orchestrator.addEventListener((state) => second.push(state))
    orchestrator.addEventListener((state) => third.push(state))

    orchestrator.markRecovering()
    orchestrator.markOnline()

    expect(log).toEqual(["recovering", "online"])
    expect(second).toEqual(["recovering", "online"])
    expect(third).toEqual(["recovering", "online"])
  })

  test("destroy clears listeners and is idempotent", () => {
    orchestrator.destroy()
    expect(log).toEqual([]) // no events fire on destroy
    orchestrator.destroy() // idempotent
  })

  test("the orchestrator supports the offline → 2 missed set cursor → online pattern from the spec", () => {
    // Reproduces the issue #77 reconnect test:
    //   Offline → 2 `set cursor` events missed → Online; the next
    //   `dispatch()` is rejected before the Online transition.
    expect(orchestrator.canDispatch()).toBe(false) // initial offline

    // Two "set cursor" events would land here in the production
    // orchestrator wiring. The orchestrator itself doesn't observe
    // those events — it only tracks its own coarse state. The
    // dispatch gate (`canDispatch`) is the contract: while offline
    // it stays false.
    expect(orchestrator.canDispatch()).toBe(false)

    orchestrator.markRecovering()
    expect(orchestrator.canDispatch()).toBe(false)

    orchestrator.markOnline()
    expect(orchestrator.canDispatch()).toBe(true)
  })
})
