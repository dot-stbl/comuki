import { createContext, useContext, type ReactNode } from "react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, waitFor } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { PART_KINDS } from "@/domains/chat/model/parts"
import type {
  ChatMessage as Message,
  MessagePart,
  PartKind,
} from "@/domains/chat/model/types"
import { ChatMessage } from "@/domains/chat/ui/chat-message"
import { TestSession } from "@/shared/session/test-session"

/**
 * The renderer table, and the property that made it worth writing.
 *
 * The compile-time half — a kind with no arm stops the table's own
 * declaration from type-checking — cannot be asserted from a test, because a
 * test that failed to compile would not run. What *can* be asserted is the
 * runtime consequence the old chain of `&&`s got wrong: **every kind in the
 * frozen list draws something.** The last case below walks `PART_KINDS` and
 * fails on any kind that renders an empty body, which is exactly the defect
 * the four sequential conditionals produced and hid.
 */

/* Warm the markdown chunk. `MessageProse` reaches the parser through
   `lazy()`, and compiling react-markdown's whole unified pipeline for the
   first time is easily a second on a loaded machine — long enough to lose a
   race against `waitFor` and turn a real assertion into a flake. Importing it
   here puts it in the module cache; the component still goes through the same
   `lazy()` boundary it does in the product. */
beforeAll(async () => {
  await import("@/domains/chat/ui/message-markdown")
})

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/chat", "/runs", "/runs/$runId", "/queue", "/tasks"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

/**
 * A message on its own, inside the two providers it cannot render without.
 *
 * A memory router, because a resolved identifier and a hand-off are both
 * `<Link>`s; a session, because both ask a permission before they render. The
 * router loads its first match asynchronously, so mounting is awaited — the
 * same shape `chat-page.test.tsx` uses.
 */
async function mount(message: Message) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  })

  const view = render(
    <TestSession roles={["operator"]}>
      <SlotContext
        value={
          <ol>
            <ChatMessage message={message} onDecide={vi.fn()} />
          </ol>
        }
      >
        <RouterProvider router={router} />
      </SlotContext>
    </TestSession>
  )

  await waitFor(() => expect(at("chat-message")).not.toBeNull())
  return view
}

/* The suite addresses the DOM by `data-test`, the attribute this product
   stamps — testing-library's own `getByTestId` looks for `data-testid` and
   would find nothing. */
const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

function turn(parts: MessagePart[]): Message {
  return { id: "m1", kind: "reply", at: "09:00", parts }
}

/** One of every kind, so the walk below has something to draw for each. */
const SAMPLES: { [K in PartKind]: Extract<MessagePart, { kind: K }> } = {
  text: { kind: "text", markdown: "a **bold** claim about 8f3c2a91" },
  code: { kind: "code", language: "ts", source: "const a = 1\n" },
  diagram: { kind: "diagram", dialect: "mermaid", source: "flowchart LR\n a-->b" },
  thinking: { kind: "thinking", text: "weighing two options", tokens: 1840 },
  tool: {
    kind: "tool",
    name: "runs.get",
    inputJson: '{"run":"8f3c2a91"}',
    status: "success",
    outputJson: "status=running",
    durationMs: 412,
  },
  handoff: { kind: "handoff", query: "waiting" },
  plan: {
    kind: "plan",
    nodes: [
      { id: "w1", label: "read the ticket", profile: "explorer" },
      { id: "w2", label: "write the patch", profile: "implementer" },
    ],
    edges: [{ from: "w1", to: "w2" }],
  },
}

describe("each part kind draws itself", () => {
  it("renders prose as markdown once the parser has loaded", async () => {
    await mount(turn([SAMPLES.text]))

    // The fallback is the plain paragraph, so the words are on screen from
    // the first frame; the parsed reading arrives with the chunk.
    expect(document.body.textContent).toContain("claim about 8f3c2a91")

    const prose = await waitFor(
      () => {
        const found = at("chat-markdown")
        expect(found).not.toBeNull()
        return found as HTMLElement
      },
      { timeout: 5000 }
    )
    expect(prose.querySelector("strong")?.textContent).toBe("bold")
  })

  it("renders a fenced block as the kit's own primitive", async () => {
    await mount(turn([SAMPLES.code]))

    const block = at("code-block") as HTMLElement
    expect(block.getAttribute("data-language")).toBe("ts")
    expect(at("code-block-body")?.textContent).toContain("const a = 1")
  })

  it("says a diagram is a diagram rather than pretending to draw one", async () => {
    await mount(turn([SAMPLES.diagram]))

    const stub = at("chat-diagram") as HTMLElement
    expect(stub.textContent).toContain("mermaid")
    // The source is still reachable — a stub that hides the content would be
    // worse than no renderer at all.
    expect(
      stub.querySelector("[data-test='code-block-body']")?.textContent
    ).toContain("flowchart")
  })

  it("folds the model's working-out away and leaves it closed", async () => {
    await mount(turn([SAMPLES.thinking]))

    const details = at("chat-thinking") as HTMLDetailsElement
    expect(details.tagName).toBe("DETAILS")
    expect(details.open).toBe(false)
    expect(details.textContent).toContain("1,840 tokens")
  })

  it("draws a tool call as a record, with how long it took", async () => {
    await mount(turn([SAMPLES.tool]))

    expect(at("chat-tool")?.getAttribute("data-status")).toBe("success")
    expect(at("chat-tool-duration")?.textContent).toBe("412ms")
    expect(at("chat-tool-result")?.textContent).toContain("status=running")
  })

  it("reads a plan out in order, with the dependency on the node that waits", async () => {
    await mount(turn([SAMPLES.plan]))

    const plan = at("chat-plan") as HTMLElement
    expect(plan.querySelectorAll("li")).toHaveLength(2)
    expect(plan.textContent).toContain("after read the ticket")
  })

  it("hands a question off to the screen that answers it", async () => {
    await mount(turn([SAMPLES.handoff]))
    expect(at("chat-handoffs")).not.toBeNull()
  })
})

describe("a turn with nothing in it", () => {
  it("says so, instead of rendering a byline over a blank row", async () => {
    await mount({ id: "m1", kind: "tool", at: "09:00" })
    expect(at("chat-blank")).not.toBeNull()
  })
})

describe("the table has an arm for every kind in the frozen list", () => {
  // The compile-time guard is the table's own declaration; this is the
  // runtime half of the same promise, and the one the old `&&` chain broke.
  it.each(PART_KINDS.map((kind) => [kind] as const))(
    "%s renders something",
    async (kind) => {
      const { unmount } = await mount(turn([SAMPLES[kind]]))

      // The body is the last child of the row — the byline is the first.
      const body = at("chat-message")?.lastElementChild
      expect(at("chat-blank")).toBeNull()
      expect((body?.textContent ?? "").trim().length).toBeGreaterThan(0)

      unmount()
    }
  )
})
