import { describe, expect, it } from "bun:test"
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
