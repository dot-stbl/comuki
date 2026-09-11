import { fireEvent, render } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

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

/* The virtualizer measures its scroll port with `offsetHeight`, and jsdom
   reports zero for every box — with an outer size of zero it renders no
   window at all, which would make the virtualized cases below assert nothing
   rather than assert the wrong thing. One height, stated once, is what lets
   the branch be tested; `chat-page.test.tsx` stubs the same pair for the same
   reason. */
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 480,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 900,
  })
})

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

/* ------------------------------------------------------------------ *
 * The scroll, and who it belongs to.
 *
 * jsdom lays nothing out: every box is zero tall and `scrollTop` is a
 * property with no layout box behind it, so setting it is a no-op. The port
 * is therefore instrumented — a real getter and setter over a variable, plus
 * the two heights — which makes the component's own writes observable and is
 * the only way this behaviour is testable at all.
 * ------------------------------------------------------------------ */

interface Port {
  /** Where the component last put the scroll. */
  top: () => number
  /** Put the operator somewhere, the way a wheel would. */
  scrollTo: (top: number) => void
}

function instrument(height: number, view: number): Port {
  const node = at("chat-scroll") as HTMLElement
  let top = 0

  Object.defineProperty(node, "scrollTop", {
    configurable: true,
    get: () => top,
    set: (next: number) => {
      top = next
    },
  })
  Object.defineProperty(node, "scrollHeight", {
    configurable: true,
    get: () => height,
  })
  Object.defineProperty(node, "clientHeight", {
    configurable: true,
    get: () => view,
  })

  return {
    top: () => top,
    scrollTo: (next: number) => {
      top = next
      fireEvent.scroll(node)
    },
  }
}

describe("the scroll follows the operator, not the data", () => {
  it("stays pinned to the newest turn while it is already at the bottom", () => {
    const { rerender } = mount([message("m1")])
    const port = instrument(1000, 300)

    rerender(
      <TestSession>
        <ChatThread messages={[message("m1"), message("m2")]} onDecide={vi.fn()} />
      </TestSession>
    )

    expect(port.top()).toBe(1000)
    expect(at("chat-jump")).toBeNull()
  })

  it("leaves the operator where they are once they have scrolled up", () => {
    // The defect this replaces: `scrollTop = scrollHeight` on every change
    // dragged somebody reading a tool result back to the bottom on the next
    // token, several times a second.
    const { rerender } = mount([message("m1")])
    const port = instrument(1000, 300)

    port.scrollTo(120)
    rerender(
      <TestSession>
        <ChatThread messages={[message("m1"), message("m2")]} onDecide={vi.fn()} />
      </TestSession>
    )

    expect(port.top()).toBe(120)
  })

  it("offers the way back, and only while there is a way back to offer", () => {
    mount([message("m1")])
    const port = instrument(1000, 300)

    expect(at("chat-jump")).toBeNull()

    port.scrollTo(120)
    const jump = at("chat-jump")
    expect(jump).not.toBeNull()

    fireEvent.click(jump as HTMLElement)
    expect(port.top()).toBe(1000)
    expect(at("chat-jump")).toBeNull()
  })

  it("counts a near-bottom position as the bottom", () => {
    // A thread that unpins the instant a wheel moves by a pixel is a thread
    // that stops following for no reason the operator would recognise.
    mount([message("m1")])
    const port = instrument(1000, 300)

    port.scrollTo(680) // 20px from the end
    expect(at("chat-jump")).toBeNull()

    port.scrollTo(600) // 100px from the end
    expect(at("chat-jump")).not.toBeNull()
  })
})

/* ------------------------------------------------------------------ *
 * Virtualization, past the point where it is worth it.
 * ------------------------------------------------------------------ */

const thread = (count: number) =>
  Array.from({ length: count }, (_, index) => message(`m${index}`))

describe("the log virtualizes only when the thread is long", () => {
  it("draws every turn of an ordinary conversation", () => {
    mount(thread(12))

    const log = at("chat-log")
    expect(log?.getAttribute("data-virtualized")).toBeNull()
    expect(log?.querySelectorAll("[data-test='chat-message']")).toHaveLength(12)
  })

  it("draws a window of a long one, and reserves the rest as height", () => {
    mount(thread(120))

    const log = at("chat-log")
    expect(log?.getAttribute("data-virtualized")).toBe("true")

    const drawn = log?.querySelectorAll("[data-test='chat-message']").length ?? 0
    expect(drawn).toBeGreaterThan(0)
    expect(drawn).toBeLessThan(120)

    // The turns that are not mounted are still measured by the scrollbar:
    // a spacer stands for them, and it is hidden because it is geometry.
    const spacers = log?.querySelectorAll("[aria-hidden='true']") ?? []
    expect(spacers.length).toBeGreaterThan(0)
    expect(
      [...spacers].some((node) =>
        (node.getAttribute("style") ?? "").includes("--spacer")
      )
    ).toBe(true)
  })

  it("keeps the log's own semantics whichever reading it is in", () => {
    mount(thread(120))
    const log = at("chat-log")

    expect(log?.getAttribute("role")).toBe("log")
    expect(log?.getAttribute("aria-live")).toBe("polite")
    expect(log?.getAttribute("aria-label")).toBe("Conversation")
  })

  it("still renders the reply in flight outside the log", () => {
    mount([...thread(120), message("m_last", { streaming: true })])

    const log = at("chat-log")
    const pending = at("chat-streaming")
    expect(pending).not.toBeNull()
    expect(log?.contains(pending as Node)).toBe(false)
  })
})
