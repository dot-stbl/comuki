import { render } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { ChatMessage } from "@/domains/chat/model/types"
import { ChatThread } from "@/domains/chat/ui/chat-thread"
import { TestSession } from "@/shared/session/test-session"

/**
 * The accessibility decision, asserted — because it is the one a chat gets
 * wrong by default and the one nothing else in the suite would catch.
 *
 * A streaming reply that pushes every token into a live region reads the same
 * growing sentence from the start, over and over, and there is no gesture that
 * stops it. The composition's answer is a partition: finished messages live in
 * a `role="log"` and are announced once; the reply in flight lives outside it
 * and is hidden, with a single `role="status"` saying that one is coming.
 */

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

function message(id: string, extra: Partial<ChatMessage> = {}): ChatMessage {
  return { id, kind: "reply", text: `body ${id}`, at: "09:00", ...extra }
}

function mount(messages: ChatMessage[]) {
  return render(
    <TestSession>
      <ChatThread messages={messages} onDecide={vi.fn()} />
    </TestSession>
  )
}

describe("the log", () => {
  it("is polite and announces additions only", () => {
    mount([message("m1")])
    const log = at("chat-log")

    expect(log?.getAttribute("role")).toBe("log")
    // `polite`, never `assertive`: nothing the console says is worth cutting
    // across a sentence the operator is already listening to.
    expect(log?.getAttribute("aria-live")).toBe("polite")
    expect(log?.getAttribute("aria-relevant")).toBe("additions")
    expect(log?.getAttribute("aria-label")).toBe("Conversation")
  })

  it("holds every message that has finished", () => {
    mount([message("m1"), message("m2", { kind: "person" })])
    expect(at("chat-log")?.querySelectorAll("[data-test='chat-message']"))
      .toHaveLength(2)
  })
})

describe("a reply in flight", () => {
  const messages = [message("m1"), message("m2", { streaming: true })]

  it("is rendered outside the log, so it is not announced per token", () => {
    mount(messages)

    const log = at("chat-log")
    const pending = at("chat-streaming")

    expect(pending).not.toBeNull()
    expect(log?.contains(pending as Node)).toBe(false)
    expect(log?.querySelectorAll("[data-test='chat-message']")).toHaveLength(1)
  })

  it("is hidden from assistive technology while it arrives", () => {
    mount(messages)
    expect(at("chat-streaming")?.getAttribute("aria-hidden")).toBe("true")
  })

  it("says once that a reply is coming", () => {
    mount(messages)
    const status = at("chat-announce")
    expect(status?.getAttribute("role")).toBe("status")
    expect(status?.textContent).toBe("the assistant is replying")
  })

  it("joins the log, and the announcement empties, once it settles", () => {
    const { rerender } = mount(messages)

    rerender(
      <TestSession>
        <ChatThread
          messages={[message("m1"), message("m2")]}
          onDecide={vi.fn()}
        />
      </TestSession>
    )

    // Now it is an ordinary addition to a polite log: read once, in full.
    expect(at("chat-streaming")).toBeNull()
    expect(at("chat-log")?.querySelectorAll("[data-test='chat-message']"))
      .toHaveLength(2)
    expect(at("chat-announce")?.textContent).toBe("")
  })
})

describe("an empty conversation", () => {
  it("says what the console is instead of showing nothing", () => {
    mount([])
    expect(at("chat-empty")).not.toBeNull()
    expect(at("chat-announce")?.textContent).toBe("")
  })
})

describe("the five states each render as themselves", () => {
  it("draws a tool call, a proposal and an error differently", () => {
    mount([
      message("m1", { kind: "person", text: "стой" }),
      message("m2", {
        kind: "tool",
        text: undefined,
        tool: { name: "runs.get", args: "run=8f3c2a91", status: "failed", result: "504" },
      }),
      message("m3", {
        kind: "proposal",
        text: undefined,
        proposal: {
          id: "cp_1",
          act: "run.stop",
          summary: "stop it",
          projectId: "p_test",
        },
      }),
      message("m4", { kind: "error", text: "the turn failed" }),
    ])

    expect(at("chat-tool")?.getAttribute("data-status")).toBe("failed")
    expect(at("chat-proposal")).not.toBeNull()
    // The error is the one message that announces itself.
    expect(at("chat-error")?.getAttribute("role")).toBe("alert")
  })
})
