import { describe, expect, it } from "bun:test"
import { LogLevel, NullLogger, type RetryContext } from "@microsoft/signalr"
import {
  bindChatEvents,
  createRetryGate,
  hubStateFor,
  isHubAuthFailure,
  neverGiveUpRetryPolicy,
  RealtimeTransportMethods,
  reconnectRetryDelayMs,
  rejoinChatGroups,
} from "./signalr"

function stubConnection() {
  const handlers = new Map<string, (payload: unknown) => void>()
  return {
    on: (method: string, handler: (payload: unknown) => void) => {
      handlers.set(method, handler)
    },
    emit: (method: string, payload: unknown) => {
      handlers.get(method)?.(payload)
    },
  }
}

describe("bindChatEvents", () => {
  it("routes ChatChunk and ChatTurnComplete to their callbacks with unknown frames", () => {
    // Frames arrive as `unknown` — the test asserts the *raw* payload is
    // forwarded unchanged. The kernel adapter owns validation through
    // decodeChatChunk/decodeChatTurnComplete.
    const connection = stubConnection()
    const chunks: unknown[] = []
    const outcomes: unknown[] = []
    bindChatEvents(
      connection,
      (frame) => chunks.push(frame),
      (frame) => outcomes.push(frame)
    )

    const chunkA = { sessionId: "s1", seq: 1, text: "План " }
    const chunkB = { sessionId: "s1", seq: 2, text: "рефакторинга" }
    const complete = { sessionId: "s1", outcome: "awaiting_approval" }

    connection.emit(RealtimeTransportMethods.ChatChunk, chunkA)
    connection.emit(RealtimeTransportMethods.ChatChunk, chunkB)
    connection.emit(RealtimeTransportMethods.ChatTurnComplete, complete)

    expect(chunks).toEqual([chunkA, chunkB])
    expect(outcomes).toEqual([complete])
  })
})

describe("reconnectRetryDelayMs", () => {
  it("maps the retry count onto the 0/2/5/10s ramp then flat 30s", () => {
    expect(reconnectRetryDelayMs(0)).toBe(0)
    expect(reconnectRetryDelayMs(1)).toBe(2_000)
    expect(reconnectRetryDelayMs(2)).toBe(5_000)
    expect(reconnectRetryDelayMs(3)).toBe(10_000)
    expect(reconnectRetryDelayMs(4)).toBe(30_000)
    expect(reconnectRetryDelayMs(11)).toBe(30_000)
  })
})

describe("neverGiveUpRetryPolicy", () => {
  const context = (previousRetryCount: number): RetryContext => ({
    previousRetryCount,
    elapsedMilliseconds: 1_000,
    retryReason: new Error("socket dropped"),
  })

  it("delegates to the ramp", () => {
    expect(
      neverGiveUpRetryPolicy.nextRetryDelayInMilliseconds(context(2))
    ).toBe(5_000)
    expect(
      neverGiveUpRetryPolicy.nextRetryDelayInMilliseconds(context(3))
    ).toBe(10_000)
  })

  it("never surrenders — every retry count yields a finite delay", () => {
    for (let previousRetryCount = 0; previousRetryCount <= 12; previousRetryCount++) {
      const delay = neverGiveUpRetryPolicy.nextRetryDelayInMilliseconds(
        context(previousRetryCount)
      )
      expect(delay).toBeGreaterThanOrEqual(0)
      expect(Number.isFinite(delay)).toBe(true)
    }
  })
})

describe("hubStateFor", () => {
  it("maps lifecycle events onto status-bar states", () => {
    expect(hubStateFor("connecting")).toBe("connecting")
    expect(hubStateFor("started")).toBe("live")
    expect(hubStateFor("reconnected")).toBe("live")
    expect(hubStateFor("reconnecting")).toBe("reconnecting")
    expect(hubStateFor("closed")).toBe("offline")
  })
})

describe("rejoinChatGroups", () => {
  it("joins every session after a reconnect, swallowing per-group rejections", async () => {
    const joined: string[] = []
    const connection = {
      invoke: (method: string, sessionId: string): Promise<unknown> => {
        if (sessionId === "deleted") {
          return Promise.reject(new Error("not a member"))
        }
        joined.push(`${method}:${sessionId}`)
        return Promise.resolve(undefined)
      },
    }

    await rejoinChatGroups(connection, ["s1", "deleted", "s2"])

    expect(joined).toEqual(["JoinChatAsync:s1", "JoinChatAsync:s2"])
  })
})

describe("isHubAuthFailure", () => {
  it("recognises statusCode / status 401 and 403", () => {
    expect(isHubAuthFailure({ statusCode: 401 })).toBe(true)
    expect(isHubAuthFailure({ status: 403 })).toBe(true)
    expect(isHubAuthFailure({ statusCode: 500 })).toBe(false)
  })

  it("recognises Unauthorized in a plain Error message", () => {
    expect(isHubAuthFailure(new Error("Failed to start: 401 Unauthorized"))).toBe(
      true
    )
    expect(isHubAuthFailure(new Error("socket dropped"))).toBe(false)
    expect(isHubAuthFailure(null)).toBe(false)
  })
})

describe("createRetryGate", () => {
  it("schedules with the 0/2/5/10/30s ramp and never stacks timers", () => {
    const scheduled: number[] = []
    const ids: Array<{ id: number; callback: () => void }> = []
    let nextId = 1
    const gate = createRetryGate(reconnectRetryDelayMs, {
      setTimeout: (callback, ms) => {
        scheduled.push(ms)
        const id = nextId++
        ids.push({ id, callback })
        return id
      },
      clearTimeout: () => {
        // unused in this test
      },
    })

    let runs = 0
    gate.schedule(() => {
      runs += 1
    })
    expect(gate.pending).toBe(true)
    expect(scheduled).toEqual([0])

    // A second schedule while a timer is pending is a no-op.
    gate.schedule(() => {
      runs += 1
    })
    expect(scheduled).toEqual([0])

    ids[0]?.callback()
    expect(runs).toBe(1)
    expect(gate.pending).toBe(false)
    expect(gate.attempt).toBe(1)

    gate.schedule(() => {
      runs += 1
    })
    expect(scheduled).toEqual([0, 2_000])
    ids[1]?.callback()
    expect(runs).toBe(2)
    expect(gate.attempt).toBe(2)

    gate.reset()
    expect(gate.attempt).toBe(0)
    gate.schedule(() => {
      runs += 1
    })
    expect(scheduled).toEqual([0, 2_000, 0])
  })

  it("cancel drops the pending timer and ignores later schedules", () => {
    const cleared: unknown[] = []
    const pending: Array<() => void> = []
    const gate = createRetryGate(() => 5_000, {
      setTimeout: (run) => {
        pending.push(run)
        return 42
      },
      clearTimeout: (id) => {
        cleared.push(id)
      },
    })
    let runs = 0
    gate.schedule(() => {
      runs += 1
    })
    gate.cancel()
    expect(cleared).toEqual([42])
    expect(gate.pending).toBe(false)
    gate.schedule(() => {
      runs += 1
    })
    expect(runs).toBe(0)
    pending[0]?.()
    expect(runs).toBe(0)
  })
})

describe("signalr logger wiring", () => {
  // Compile-time check: the canonical NullLogger from @microsoft/signalr
  // exposes a no-op .log(); assert it never throws, never echoes. If a
  // future SignalR upgrade swaps the singleton out, this test catches it.
  it("NullLogger.instance.log is a silent no-op for every LogLevel", () => {
    const levels: LogLevel[] = [
      LogLevel.Trace,
      LogLevel.Debug,
      LogLevel.Information,
      LogLevel.Warning,
      LogLevel.Error,
      LogLevel.Critical,
      LogLevel.None,
    ]
    for (const level of levels) {
      expect(() =>
        NullLogger.instance.log(
          level,
          "Failed to start the transport 'WebSockets': should be invisible"
        )
      ).not.toThrow()
    }
  })
})
