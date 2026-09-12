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
    isError: false,
    error: null as Error | null,
    refetch: vi.fn(),
  },
  transcript: {
    data: undefined as
      | { id: string; kind: string; text?: string; at: string }[]
      | undefined,
    isError: false,
    error: null as Error | null,
    refetch: vi.fn(),
  },
}))

vi.mock("@/domains/chat/api/queries", () => ({
  useChatSessionsQuery: () => wire.sessions,
  useChatMessagesQuery: () => wire.transcript,
  useChatCommandsQuery: () => ({ data: [] }),
  useSendMessageMutation: () => ({ mutate: vi.fn(), isPending: false }),
  useProposalDecisionMutation: () => ({ mutate: vi.fn(), isPending: false }),
  useStartSessionMutation: () => ({ mutate: vi.fn(), isPending: false }),
}))

beforeEach(() => {
  wire.sessions = {
    data: undefined,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }
  wire.transcript = {
    data: undefined,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }
})

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

function mountConsole() {
  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["project-admin"]}>
        <ChatConsole
          chosenId={null}
          onChosenIdChange={() => undefined}
          draft=""
          onDraftChange={() => undefined}
        />
      </TestSession>
    </ThemeProvider>
  )
}

const ONE_SESSION = [
  {
    id: "s-1",
    title: "New conversation",
    age: "just now",
    messages: [],
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
      data: [{ id: "m-1", kind: "person", text: "почему очередь стоит", at: "2026-09-13T00:00:00Z" }],
    }

    mountConsole()

    await waitFor(() => {
      expect(at("chat-composer")).not.toBeNull()
    })
    expect(at("chat-console-error")).toBeNull()
    expect(at("chat-transcript-error")).toBeNull()
  })
})
