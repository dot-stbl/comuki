import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { ChatMessage as Message } from "@/domains/chat/model/types"
import { ChatMessage } from "@/domains/chat/ui/chat-message"
import { TestSession } from "@/shared/session/test-session"

/**
 * The composition's own additions — the phase badge, the iteration beside
 * it, the metrics line under the answer, the digest chip — asserted where
 * they live: on the turn, not on any one part. A part test cannot catch a
 * badge that forgets to render, because no part owns it.
 */

function mount(message: Message) {
  return render(
    <TestSession>
      <QueryClientProvider client={new QueryClient()}>
        <ChatMessage message={message} onDecide={vi.fn()} projectId={null} />
      </QueryClientProvider>
    </TestSession>
  )
}

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

describe("the byline of an assistant turn", () => {
  it("carries a done badge once the answer has landed", () => {
    mount({
      id: "m1",
      kind: "reply",
      at: "09:00",
      parts: [
        { kind: "thinking", text: "думал" },
        { kind: "text", markdown: "ответ" },
      ],
    })

    const badge = at("chat-phase")
    expect(badge?.getAttribute("data-phase")).toBe("done")
    expect(badge?.textContent).toBe("done")
  })

  it("carries the iteration the working-out reached, beside the badge", () => {
    mount({
      id: "m1",
      kind: "reply",
      at: "09:00",
      streaming: true,
      parts: [
        { kind: "thinking", text: "iteration 1: memory.search\nдумал" },
        { kind: "text", markdown: "половина ответа" },
      ],
    })

    expect(at("chat-phase")?.getAttribute("data-phase")).toBe("thinking")
    expect(at("chat-iteration")?.textContent).toBe("iter 1")
  })

  it("says plan when the turn produced one", () => {
    mount({
      id: "m1",
      kind: "reply",
      at: "09:00",
      parts: [
        { kind: "text", markdown: "вот план" },
        {
          kind: "plan",
          nodes: [{ id: "w1", label: "шаг" }],
          edges: [],
        },
      ],
    })

    expect(at("chat-phase")?.getAttribute("data-phase")).toBe("plan")
    // No thinking part, no iteration to name.
    expect(at("chat-iteration")).toBeNull()
  })

  it("carries no badge on a person's turn", () => {
    mount({ id: "m1", kind: "person", text: "стой", at: "09:00" })
    expect(at("chat-phase")).toBeNull()
  })
})

describe("the metrics line", () => {
  it("reads the figures the turn reported, pipe-separated", () => {
    mount({
      id: "m1",
      kind: "reply",
      at: "09:00",
      meta: {
        model: "glm-4.7",
        tokensIn: 1180,
        tokensOut: 660,
        costMicros: 2900,
        latencyMs: 8200,
      },
      parts: [
        {
          kind: "tool",
          name: "runs.diff",
          inputJson: "{}",
          status: "success",
        },
      ],
    })

    expect(at("chat-metrics")?.textContent).toBe(
      "8.2s | 1 tool | 1,840 tok | $0.003"
    )
  })

  it("does not render when the turn reported nothing", () => {
    mount({ id: "m1", kind: "reply", at: "09:00", text: "просто ответ" })
    expect(at("chat-metrics")).toBeNull()
  })
})

describe("the digest row", () => {
  it("renders as the memory chip, not as a paragraph of prose", () => {
    mount({
      id: "m1",
      kind: "reply",
      at: "09:00",
      parts: [
        {
          kind: "text",
          markdown:
            "memory digest fed to the brain:\nвебхуки разбирали в смену 2026-09-12\nдо миграции не дошло",
        },
      ],
    })

    const memory = at("chat-memory") as HTMLDetailsElement
    expect(memory).not.toBeNull()
    expect(memory.tagName).toBe("DETAILS")
    expect(memory.open).toBe(false)
    expect(memory.textContent).toContain("2 facts")
    // The turn is context the console fed the brain — it gets no phase badge
    // and no metrics line of its own.
    expect(at("chat-phase")).toBeNull()
    expect(at("chat-metrics")).toBeNull()
  })
})
