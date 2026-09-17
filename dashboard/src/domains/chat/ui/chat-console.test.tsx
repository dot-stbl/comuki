import { useState } from "react"
import { fireEvent, render, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { ChatConsole } from "@/domains/chat/ui/chat-console"
import { TestSession } from "@/shared/session/test-session"

/* The queries are the only thing under variation here, so they are the only
   thing mocked — the console's real children (thread, composer, rail) render
   for real, which is what makes "the error replaced the column" a claim
   about the console rather than about a stub. */
const wire = vi.hoisted(() => ({
  sessions: {
    data: undefined as
      | { id: string; title: string; age: string; messages: unknown[] }[]
      | undefined,
    isLoading: false,
    isError: false,
    error: null as Error | null,
    refetch: vi.fn(),
  },
  transcript: {
    data: undefined as
      { id: string; kind: string; text?: string; at: string }[] | undefined,
    isLoading: false,
    isError: false,
    error: null as Error | null,
    refetch: vi.fn(),
  },
  /* The send, as the console actually meets it: a mutation whose failure
     arrives in a later task. That gap is the whole point — the composer has
     already emptied the box by then, and the restore has to happen against
     what the box holds *now* rather than against what it held at the gesture. */
  send: {
    sent: vi.fn(),
    fail: null as Error | null,
  },
}))

vi.mock("@/domains/chat/api/queries", () => ({
  useChatSessionsQuery: () => wire.sessions,
  useChatMessagesQuery: () => wire.transcript,
  useChatCommandsQuery: () => ({ data: [] }),
  useSendMessageMutation: () => ({
    isPending: false,
    mutate: (
      input: unknown,
      options?: { onError?: (error: unknown) => void }
    ) => {
      wire.send.sent(input)
      const failure = wire.send.fail
      if (failure) {
        setTimeout(() => options?.onError?.(failure), 0)
      }
    },
  }),
  useProposalDecisionMutation: () => ({ mutate: vi.fn(), isPending: false }),
  useStartSessionMutation: () => ({ mutate: vi.fn(), isPending: false }),
}))

beforeEach(() => {
  wire.sessions = {
    data: undefined,
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }
  wire.transcript = {
    data: undefined,
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }
  wire.send = { sent: vi.fn(), fail: null }
})

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

/* The draft and the open conversation belong to the *container*, so the
   harness holds them the way the route and the dock hold them — in state. A
   spy would not do: half these assertions are about what is in the box after
   the console has put something back into it. */
function Harness() {
  const [draft, setDraft] = useState("")
  const [chosen, setChosen] = useState<string | null>(null)
  return (
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["project-admin"]}>
        <ChatConsole
          chosenId={chosen}
          onChosenIdChange={setChosen}
          draft={draft}
          onDraftChange={setDraft}
        />
      </TestSession>
    </ThemeProvider>
  )
}

function mountConsole() {
  return render(<Harness />)
}

const box = () => at("chat-input") as HTMLTextAreaElement

async function typeAndSend(text: string) {
  const input = await waitFor(() => {
    const found = at("chat-input")
    expect(found).not.toBeNull()
    return found as HTMLTextAreaElement
  })
  fireEvent.change(input, { target: { value: text } })
  fireEvent.keyDown(input, { key: "Enter" })
}

const ONE_SESSION = [
  {
    id: "s-1",
    title: "New conversation",
    age: "just now",
    messages: [],
  },
]

const TWO_SESSIONS = [
  ...ONE_SESSION,
  { id: "s-2", title: "Yesterday", age: "1d ago", messages: [] },
]

const ONE_TURN = [
  {
    id: "m-1",
    kind: "person",
    text: "почему очередь стоит",
    at: "2026-09-13T00:00:00Z",
  },
]

describe("the console against a dead read", () => {
  it("says the console did not load rather than rendering an empty one", async () => {
    // A swallowed sessions error renders "no conversations" for a dead wire
    // — indistinguishable from the truth, and therefore a lie. The panel is
    // the pages' own shape: named state, the wire's sentence, one retry.
    wire.sessions = {
      ...wire.sessions,
      isError: true,
      error: new Error("inbox wire is down"),
    }

    mountConsole()

    const panel = await waitFor(() => {
      const found = at("chat-console-error")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(panel.getAttribute("role")).toBe("alert")
    expect(panel.textContent).toContain("The console did not load")
    expect(panel.textContent).toContain("inbox wire is down")

    fireEvent.click(at("chat-console-error-retry") as HTMLElement)
    expect(wire.sessions.refetch).toHaveBeenCalled()
  })

  it("takes the thread's place when the transcript fails with nothing to fall back on", async () => {
    wire.sessions = { ...wire.sessions, data: ONE_SESSION }
    wire.transcript = {
      ...wire.transcript,
      isError: true,
      error: new Error("messages endpoint returned 500"),
    }

    mountConsole()

    const panel = await waitFor(() => {
      const found = at("chat-transcript-error")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(panel.textContent).toContain("The transcript did not load")
    expect(panel.textContent).toContain("messages endpoint returned 500")
    // The composer is not drawn beside a thread it cannot show.
    expect(at("chat-composer")).toBeNull()

    fireEvent.click(at("chat-transcript-error-retry") as HTMLElement)
    expect(wire.transcript.refetch).toHaveBeenCalled()
  })

  it("renders the console, not a panel, when both reads answer", async () => {
    wire.sessions = { ...wire.sessions, data: ONE_SESSION }
    wire.transcript = {
      ...wire.transcript,
      data: [
        {
          id: "m-1",
          kind: "person",
          text: "почему очередь стоит",
          at: "2026-09-13T00:00:00Z",
        },
      ],
    }

    mountConsole()

    await waitFor(() => {
      expect(at("chat-composer")).not.toBeNull()
    })
    expect(at("chat-console-error")).toBeNull()
    expect(at("chat-transcript-error")).toBeNull()
  })
})

describe("the console while the read is still in flight", () => {
  it("draws the shape of the thread rather than the words for an empty one", async () => {
    // "Nothing said yet" over a conversation that is on its way is the
    // console answering a question it has not asked yet.
    wire.sessions = { ...wire.sessions, isLoading: true }

    mountConsole()

    await waitFor(() => {
      expect(at("chat-console-loading")).not.toBeNull()
    })
    expect(at("chat-empty")).toBeNull()
    expect(at("chat-sessions-empty")).toBeNull()
    expect(at("chat-sessions-loading")).not.toBeNull()
  })

  it("says the rail is empty only once the list has answered", async () => {
    wire.sessions = { ...wire.sessions, data: [] }

    mountConsole()

    await waitFor(() => {
      expect(at("chat-sessions-empty")).not.toBeNull()
    })
    expect(at("chat-sessions-loading")).toBeNull()
    // A dead read is the console's sentence, not the rail's — the rail says
    // nothing rather than answering it with "no conversations yet".
    expect(at("chat-console-error")).toBeNull()
  })

  it("leaves the rail silent when the read failed, because the centre says it", async () => {
    wire.sessions = {
      ...wire.sessions,
      isError: true,
      error: new Error("inbox wire is down"),
    }

    mountConsole()

    await waitFor(() => {
      expect(at("chat-console-error")).not.toBeNull()
    })
    expect(at("chat-sessions-empty")).toBeNull()
  })
})

describe("a send the wire refused", () => {
  it("gives the words back to the box and says why", async () => {
    wire.sessions = { ...wire.sessions, data: ONE_SESSION }
    wire.transcript = { ...wire.transcript, data: ONE_TURN }
    wire.send.fail = new Error("the inbox refused the message")

    mountConsole()
    await typeAndSend("останови прогон")

    const panel = await waitFor(() => {
      const found = at("chat-send-error")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(panel.getAttribute("role")).toBe("alert")
    expect(panel.textContent).toContain("the inbox refused the message")
    // The whole point: the message is not gone.
    expect(box().value).toBe("останови прогон")
    // And it went back where it was typed, so it is not repeated in the band.
    expect(at("chat-send-error-unsent")).toBeNull()
  })

  it("does not paste the old message over a thought the operator started since", async () => {
    wire.sessions = { ...wire.sessions, data: ONE_SESSION }
    wire.transcript = { ...wire.transcript, data: ONE_TURN }
    wire.send.fail = new Error("the inbox refused the message")

    mountConsole()
    await typeAndSend("останови прогон")
    // Typed while the request was in flight. It is newer than the message
    // that failed, and overwriting it would be a second loss.
    fireEvent.change(box(), { target: { value: "нет, сначала посмотри лог" } })

    await waitFor(() => {
      expect(at("chat-send-error-unsent")).not.toBeNull()
    })
    expect(box().value).toBe("нет, сначала посмотри лог")
    expect(at("chat-send-error-unsent")?.textContent).toBe("останови прогон")
  })

  it("keeps the refusal on the conversation it happened in", async () => {
    wire.sessions = { ...wire.sessions, data: TWO_SESSIONS }
    wire.transcript = { ...wire.transcript, data: ONE_TURN }
    wire.send.fail = new Error("the inbox refused the message")

    mountConsole()
    await typeAndSend("останови прогон")
    await waitFor(() => {
      expect(at("chat-send-error")).not.toBeNull()
    })

    // A failure raised against one thread must not appear over another, where
    // it would name something that never happened there.
    const other = document.querySelector<HTMLElement>(
      '[data-test="chat-session"][data-session="s-2"]'
    )
    fireEvent.click(other as HTMLElement)

    await waitFor(() => {
      expect(at("chat-send-error")).toBeNull()
    })
  })
})
