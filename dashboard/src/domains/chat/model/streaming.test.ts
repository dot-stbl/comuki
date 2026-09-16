import { afterEach, describe, expect, it, vi } from "vitest"

import {
  applyChatChunk,
  beginChatTurn,
  chatTurnStreamStore,
  completeChatTurn,
  pendingUserMessageOf,
  resetChatTurnStream,
  streamingReplyMessageOf,
} from "@/domains/chat/model/streaming"

/**
 * The live overlay of a running chat turn, held to the same contract the
 * host journals: fragments arrive one by one, join with newlines into the
 * thinking part, and the whole overlay dissolves on the terminal signal —
 * never before it, never partially.
 */

const SESSION = "11111111-1111-4111-8111-111111111111"
const OTHER = "22222222-2222-4222-8222-222222222222"

afterEach(() => {
  resetChatTurnStream()
})

describe("beginChatTurn", () => {
  it("opens the turn with the sent text and no fragments — the reply row does not exist yet", () => {
    beginChatTurn(SESSION, "what broke yesterday?")

    const { stream } = chatTurnStreamStore.getSnapshot()
    expect(stream).not.toBeNull()
    expect(stream!.userText).toBe("what broke yesterday?")
    expect(stream!.fragments).toEqual([])
    expect(streamingReplyMessageOf(stream!)).toBeNull()

    const user = pendingUserMessageOf(stream!)
    expect(user.kind).toBe("person")
    expect(user.text).toBe("what broke yesterday?")
  })

  it("notifies subscribers when the turn opens", () => {
    const listener = vi.fn()
    chatTurnStreamStore.subscribe(listener)

    beginChatTurn(SESSION, "hello")

    expect(listener).toHaveBeenCalledTimes(1)
  })
})

describe("applyChatChunk", () => {
  it("accumulates fragments in arrival order and joins them with newlines in the thinking part", () => {
    beginChatTurn(SESSION, "hello")
    applyChatChunk({ sessionId: SESSION, seq: 0, text: "iteration 1: reading memory" })
    applyChatChunk({ sessionId: SESSION, seq: 1, text: "memory.search(\"deploy notes\")" })

    const { stream } = chatTurnStreamStore.getSnapshot()
    const reply = streamingReplyMessageOf(stream!)
    expect(reply).not.toBeNull()
    expect(reply!.streaming).toBe(true)
    expect(reply!.kind).toBe("reply")
    expect(reply!.parts).toEqual([
      { kind: "thinking", text: "iteration 1: reading memory\nmemory.search(\"deploy notes\")" },
    ])
  })

  it("drops fragments of a turn nobody opened — a foreign session cannot conjure an overlay", () => {
    beginChatTurn(SESSION, "hello")

    applyChatChunk({ sessionId: OTHER, seq: 0, text: "other tab's turn" })

    const { stream } = chatTurnStreamStore.getSnapshot()
    expect(stream!.fragments).toEqual([])
    expect(stream!.sessionId).toBe(SESSION)
  })
})

describe("completeChatTurn", () => {
  it("dissolves the whole overlay — user row, fragments, everything", () => {
    beginChatTurn(SESSION, "hello")
    applyChatChunk({ sessionId: SESSION, seq: 0, text: "thinking..." })

    completeChatTurn(SESSION)

    expect(chatTurnStreamStore.getSnapshot().stream).toBeNull()
  })

  it("ignores the terminal signal of another session's turn", () => {
    beginChatTurn(SESSION, "hello")

    completeChatTurn(OTHER)

    expect(chatTurnStreamStore.getSnapshot().stream).not.toBeNull()
  })
})

describe("streaming reply clock", () => {
  it("carries a local HH:MM stamp — the same shape a journaled row renders", () => {
    beginChatTurn(SESSION, "hello")
    applyChatChunk({ sessionId: SESSION, seq: 0, text: "step" })

    const reply = streamingReplyMessageOf(chatTurnStreamStore.getSnapshot().stream!)

    expect(reply!.at).toMatch(/^\d{2}:\d{2}$/)
  })
})
