import { describe, expect, it } from "vitest"

import {
  hasRenderableBody,
  messageParts,
  PART_KINDS,
  toolCallToPart,
} from "@/domains/chat/model/parts"
import type { ChatMessage, MessagePart } from "@/domains/chat/model/types"

/**
 * What a turn is made of — the derivation, tested without a DOM.
 *
 * This is where the flat message and the part list are reconciled, so it is
 * where the latent hole in the old composition was closed: `kind === "tool" &&
 * message.tool` drew *nothing at all* when a tool message carried no tool
 * record, and nothing in the type system or the suite said so.
 */

function message(extra: Partial<ChatMessage> = {}): ChatMessage {
  return { id: "m1", kind: "reply", at: "09:00", ...extra }
}

describe("an explicit part list", () => {
  it("wins outright — a turn that said what it is made of is not second-guessed", () => {
    const parts: MessagePart[] = [{ kind: "text", markdown: "**bold**" }]
    const derived = messageParts(
      message({ parts, text: "ignored", handoff: "waiting" })
    )

    expect(derived).toBe(parts)
  })

  it("may be empty, and an empty list is a body with nothing in it", () => {
    expect(messageParts(message({ parts: [] }))).toEqual([])
    expect(hasRenderableBody(message({ parts: [] }))).toBe(false)
  })
})

describe("the flat message, derived", () => {
  it("reads prose as a text part", () => {
    expect(messageParts(message({ kind: "person", text: "стой" }))).toEqual([
      { kind: "text", markdown: "стой" },
    ])
  })

  it("puts the hand-off under the prose, the order it has always been drawn in", () => {
    const parts = messageParts(
      message({ text: "шесть прогонов", handoff: "waiting" })
    )
    expect(parts.map((part) => part.kind)).toEqual(["text", "handoff"])
  })

  it("converts a tool record into the part shape the wire will send", () => {
    const parts = messageParts(
      message({
        kind: "tool",
        tool: {
          name: "runs.get",
          args: "run=8f3c2a91",
          status: "failed",
          result: "504",
        },
      })
    )

    expect(parts).toEqual([
      {
        kind: "tool",
        name: "runs.get",
        inputJson: "run=8f3c2a91",
        status: "failed",
        outputJson: "504",
      },
    ])
  })

  it("puts the tool record before the prose that explains it", () => {
    const parts = messageParts(
      message({
        kind: "tool",
        text: "пул недоступен",
        tool: { name: "queue.workers", args: "", status: "failed" },
      })
    )
    expect(parts.map((part) => part.kind)).toEqual(["tool", "text"])
  })
})

describe("a tool message with no tool record — the hole in the old composition", () => {
  it("falls back to whatever prose it has, instead of drawing an empty row", () => {
    // The old dispatch read `kind === "tool" && message.tool` for the record
    // and `kind === "person" || kind === "reply"` for the prose, so a message
    // in this shape matched neither and rendered a byline over nothing.
    const parts = messageParts(
      message({ kind: "tool", text: "the call was never journaled" })
    )

    expect(parts).toEqual([
      { kind: "text", markdown: "the call was never journaled" },
    ])
  })

  it("derives nothing when there is nothing, and says the body is empty", () => {
    const empty = message({ kind: "tool" })
    expect(messageParts(empty)).toEqual([])
    // Which is what makes the composition render a stated blank rather than a
    // hole in the log.
    expect(hasRenderableBody(empty)).toBe(false)
  })
})

describe("what the composition draws itself", () => {
  it("keeps an error out of the part list — the band is chrome, not content", () => {
    const failed = message({ kind: "error", text: "the turn failed" })
    expect(messageParts(failed)).toEqual([])
    expect(hasRenderableBody(failed)).toBe(true)
  })

  it("keeps a proposal out of the part list until it becomes a decision part", () => {
    const asked = message({
      kind: "proposal",
      proposal: {
        id: "cp_1",
        act: "run.stop",
        summary: "stop it",
        projectId: "p_test",
      },
    })
    expect(messageParts(asked)).toEqual([])
    expect(hasRenderableBody(asked)).toBe(true)
  })
})

describe("the frozen list", () => {
  it("names the seven kinds this phase ships, and neither P2 kind", () => {
    // The renderer table in `ui/message-part.tsx` is keyed on the same union,
    // so adding `question` or `decision` to the model breaks that table at
    // its declaration — and this case, which is the reminder of why.
    expect([...PART_KINDS]).toEqual([
      "text",
      "code",
      "diagram",
      "thinking",
      "tool",
      "handoff",
      "plan",
    ])
    expect(PART_KINDS).not.toContain("question")
    expect(PART_KINDS).not.toContain("decision")
  })

  it("maps the flat tool record onto the part without losing a field", () => {
    expect(
      toolCallToPart({
        name: "cost.window",
        args: "project=p_atlas",
        status: "running",
      })
    ).toEqual({
      kind: "tool",
      name: "cost.window",
      inputJson: "project=p_atlas",
      status: "running",
      outputJson: undefined,
    })
  })
})
