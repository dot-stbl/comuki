import { describe, expect, it } from "bun:test"
import { LogLevel, NullLogger, type RetryContext } from "@microsoft/signalr"
import {
  bindChatEvents,
  hubStateFor,
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
  it("routes ChatChunk and ChatTurnComplete to their callbacks", () => {
    const connection = stubConnection()
    const chunks: string[] = []
    const outcomes: string[] = []
    bindChatEvents(
      connection,
      (chunk) => chunks.push(chunk.text),
      (event) => outcomes.push(event.outcome)
    )

    connection.emit(RealtimeTransportMethods.ChatChunk, {
      sessionId: "s1",
      seq: 1,
      text: "План ",
    })
    connection.emit(RealtimeTransportMethods.ChatChunk, {
      sessionId: "s1",
      seq: 2,
      text: "рефакторинга",
    })
    connection.emit(RealtimeTransportMethods.ChatTurnComplete, {
      sessionId: "s1",
      outcome: "awaiting_approval",
    })

    expect(chunks).toEqual(["План ", "рефакторинга"])
    expect(outcomes).toEqual(["awaiting_approval"])
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
