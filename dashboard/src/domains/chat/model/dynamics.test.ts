import { describe, expect, it } from "vitest"

import {
  digestBody,
  digestFactCount,
  isMemoryDigest,
  parseThinkingSteps,
  thinkingIteration,
  turnMetrics,
  turnPhase,
} from "@/domains/chat/model/dynamics"
import type { ChatMessage } from "@/domains/chat/model/types"

/**
 * The processing dynamics, asserted on the shapes the wire really sends.
 *
 * Everything here is a pure function over a message, and the fixture below
 * mirrors the host's journaling exactly: the brain's chunks joined by
 * newlines into one thinking part, the reply after it, meta on the row. The
 * parser's job is to shape that without inventing anything — which is the
 * property every case below pins down.
 */

function reply(extra: Partial<ChatMessage> = {}): ChatMessage {
  return { id: "m1", kind: "reply", at: "09:00", ...extra }
}

describe("parseThinkingSteps", () => {
  it("splits the working-out into one step per non-empty line", () => {
    const steps = parseThinkingSteps(
      'memory.search("x") — 2 facts\n\nread_code("A.ts")\n   \nвывод: путь жив'
    )

    expect(steps).toHaveLength(3)
    expect(steps[0]?.text).toBe('memory.search("x") — 2 facts')
  })

  it("emphasizes a call-shaped prefix and keeps its tail", () => {
    const steps = parseThinkingSteps(
      'memory.search("identity refactor") 2 facts'
    )

    expect(steps[0]?.call).toBe('memory.search("identity refactor")')
    expect(steps[0]?.tail).toBe("2 facts")
  })

  it("keeps a sentence a sentence — no call it never claimed", () => {
    const steps = parseThinkingSteps("Проверил, что ключ берётся из заголовка")

    expect(steps[0]?.call).toBeUndefined()
    expect(steps[0]?.tail).toBeUndefined()
    expect(steps[0]?.text).toContain("заголовка")
  })

  it("matches a call only at the start of the line", () => {
    const steps = parseThinkingSteps(
      "упомянул memory.search(«x») по ходу мысли"
    )

    expect(steps[0]?.call).toBeUndefined()
  })

  it("accepts a dotted tool name with empty parentheses", () => {
    const steps = parseThinkingSteps("tools.list_profiles() done")

    expect(steps[0]?.call).toBe("tools.list_profiles()")
    expect(steps[0]?.tail).toBe("done")
  })

  it("answers nothing for an empty working-out", () => {
    expect(parseThinkingSteps(" \n \n")).toHaveLength(0)
  })
})

describe("thinkingIteration", () => {
  it("takes the last iteration the working-out named", () => {
    const text =
      "iteration 1: memory.search\nчто-то ещё\niteration 2: read_code"
    expect(thinkingIteration(text)).toBe(2)
  })

  it("answers undefined when no line named one", () => {
    expect(thinkingIteration("просто мысль\nещё мысль")).toBeUndefined()
  })
})

describe("turnPhase", () => {
  it("says thinking only while a streaming reply has working-out", () => {
    const message = reply({
      streaming: true,
      parts: [{ kind: "thinking", text: "думаю" }],
    })
    expect(turnPhase(message)).toBe("thinking")
  })

  it("says nothing for a streaming reply with no working-out", () => {
    // A plain token stream is not a phase the thread was told about.
    expect(turnPhase(reply({ streaming: true, text: "половина" }))).toBe("done")
  })

  it("says plan when the turn produced one", () => {
    const message = reply({
      parts: [
        { kind: "text", markdown: "вот план" },
        {
          kind: "plan",
          nodes: [{ id: "w1", label: "шаг" }],
          edges: [],
        },
      ],
    })
    expect(turnPhase(message)).toBe("plan")
  })

  it("says done for a settled reply with working-out — history does not spin", () => {
    const message = reply({
      parts: [
        { kind: "thinking", text: "думал" },
        { kind: "text", markdown: "ответ" },
      ],
    })
    expect(turnPhase(message)).toBe("done")
  })

  it("says nothing about a person's turn", () => {
    expect(
      turnPhase({ id: "m2", kind: "person", text: "стой", at: "09:01" })
    ).toBeUndefined()
  })
})

describe("turnMetrics", () => {
  it("reads latency, tools, tokens and cost off the turn", () => {
    const message = reply({
      meta: {
        model: "glm-4.7",
        tokensIn: 1180,
        tokensOut: 660,
        costMicros: 2900,
        latencyMs: 8200,
      },
      parts: [
        { kind: "thinking", text: "думал" },
        {
          kind: "tool",
          name: "runs.diff",
          inputJson: "{}",
          status: "success",
        },
        {
          kind: "tool",
          name: "runs.get",
          inputJson: "{}",
          status: "success",
        },
      ],
    })

    expect(turnMetrics(message)?.map((metric) => metric.value)).toEqual([
      "8.2s",
      "2 tools",
      "1,840 tok",
      "$0.003",
    ])
  })

  it("spells one tool without the plural", () => {
    const message = reply({
      parts: [
        { kind: "tool", name: "runs.get", inputJson: "{}", status: "success" },
      ],
    })
    expect(turnMetrics(message)?.map((metric) => metric.value)).toEqual([
      "1 tool",
    ])
  })

  it("keeps milliseconds where the difference between them matters", () => {
    expect(
      turnMetrics(reply({ meta: { latencyMs: 412 } }))?.map((m) => m.value)
    ).toEqual(["412ms"])
  })

  it("skips what was not reported rather than zeroing it", () => {
    expect(turnMetrics(reply({ meta: { latencyMs: 900 } }))).toEqual([
      { value: "900ms" },
    ])
    // No meta, no tool parts, no line at all.
    expect(turnMetrics(reply({ text: "просто ответ" }))).toBeUndefined()
  })

  it("says nothing about a person's turn", () => {
    expect(
      turnMetrics({ id: "m2", kind: "person", text: "стой", at: "09:01" })
    ).toBeUndefined()
  })
})

describe("the memory digest row", () => {
  const digest = reply({
    parts: [
      {
        kind: "text",
        markdown:
          "memory digest fed to the brain:\nвебхуки разбирали в смену 2026-09-12\nдо миграции не дошло",
      },
    ],
  })

  it("is recognised by the marker the host journals", () => {
    expect(isMemoryDigest(digest)).toBe(true)
    expect(isMemoryDigest(reply({ text: "обычный ответ" }))).toBe(false)
    expect(
      isMemoryDigest({ id: "m2", kind: "person", text: "стой", at: "09:01" })
    ).toBe(false)
  })

  it("counts the digest's own lines as its facts", () => {
    expect(digestFactCount(digest)).toBe(2)

    const empty = reply({
      parts: [{ kind: "text", markdown: "memory digest fed to the brain:\n" }],
    })
    expect(digestFactCount(empty)).toBe(0)
  })

  it("hands the body over without the marker", () => {
    expect(digestBody(digest)).not.toContain("memory digest fed")
    expect(digestBody(digest)).toContain("вебхуки разбирали")
  })
})
