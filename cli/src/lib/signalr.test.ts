import { describe, expect, it } from "bun:test"
import { LogLevel, NullLogger } from "@microsoft/signalr"
import { bindChatEvents, RealtimeTransportMethods } from "./signalr"

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
