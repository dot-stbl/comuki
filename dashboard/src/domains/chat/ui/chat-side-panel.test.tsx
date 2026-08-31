import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { BUILT_IN_COMMANDS } from "@/domains/chat/model/commands"
import type { ChatMessage } from "@/domains/chat/model/types"
import { ChatSidePanel } from "@/domains/chat/ui/chat-side-panel"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider } from "@/shared/session"

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

function proposalMessage(id: string, projectId: string): ChatMessage {
  return {
    id,
    kind: "proposal",
    at: "09:14",
    proposal: {
      id: `cp_${id}`,
      act: "plan.approve",
      summary: "approve the deploy gate",
      projectId,
      steps: [{ profile: "verifier", label: "ждать аппрува" }],
    },
  }
}

function mount(messages: ChatMessage[]) {
  render(
    <SessionProvider user={SESSION_USER_SEED} projects={PROJECTS_SEED}>
      <ChatSidePanel messages={messages} commands={BUILT_IN_COMMANDS} />
    </SessionProvider>
  )
}

describe("the side panel reads, and never decides", () => {
  it("shows the newest undecided proposal", () => {
    mount([
      { ...proposalMessage("m1", "p_comuki") },
      {
        ...proposalMessage("m2", "p_atlas"),
        proposal: {
          ...(proposalMessage("m2", "p_atlas").proposal as NonNullable<
            ChatMessage["proposal"]
          >),
          summary: "the newer one",
        },
      },
    ])

    expect(at("chat-panel-proposal")?.textContent).toContain("the newer one")
  })

  it("skips one that has already been decided", () => {
    const decided = proposalMessage("m1", "p_comuki")
    mount([
      {
        ...decided,
        proposal: { ...decided.proposal!, decision: "confirmed" },
      },
    ])
    expect(at("chat-panel-proposal")).toBeNull()
  })

  it("carries no control of its own", () => {
    // The decision is made on the card in the conversation, where the question
    // was asked. A confirm up here would be the same act in two places.
    mount([proposalMessage("m1", "p_comuki")])
    expect(document.querySelectorAll("button")).toHaveLength(0)
  })

  it("repeats the refusal a refused proposal carries", () => {
    mount([proposalMessage("m1", "p_plexor")])
    expect(at("chat-panel-proposal")?.textContent).toContain(
      "needs approver, project-admin or platform-admin on plexor"
    )
  })

  it("lists the slash commands with their descriptions", () => {
    mount([])
    const help = at("chat-panel-help")?.textContent ?? ""
    for (const command of BUILT_IN_COMMANDS) {
      expect(help).toContain(command.name)
      expect(help).toContain(command.description)
    }
  })
})
