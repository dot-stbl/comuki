import { useState } from "react"
import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { SearchTarget } from "@/app/search"
import { availableCommands } from "@/domains/chat/model/commands"
import type { SlashCommand } from "@/domains/chat/model/types"
import { ChatComposer } from "@/domains/chat/ui/chat-composer"
import type { Role, Session } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import {
  selectValues,
  setSelectValue,
} from "@/shared/ui/select/test-select"

const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

const all = (name: string) =>
  Array.from(document.querySelectorAll<HTMLElement>(`[data-test="${name}"]`))

/**
 * The client's own commands, exactly as they reach the composer: data with a
 * name and a description, declared in a repository this dashboard has never
 * heard of and cannot hardcode.
 */
const CUSTOM: SlashCommand[] = [
  {
    name: "/release",
    description: "cut a release branch and open the changelog draft",
    origin: "client",
    scope: "implied",
    permission: "inbox.take",
    projectId: "p_test",
  },
]

/** A run the dock could have seeded, pointed at the way `shapes.ts` does. */
const RUN_SEED: SearchTarget = {
  kind: "run",
  id: "5b1d7e40",
  href: "/runs/5b1d7e40",
  permission: "runs.view",
}

/** The same two projects `TestSession` provides, for building the command list. */
function sessionFor(
  roles: Role[],
  projectRoles: Record<string, Role[]>
): Session {
  return {
    user: {
      id: "u_test",
      name: "Test User",
      email: "test@comuki.local",
      platformRoles: roles,
      projectRoles,
    },
    projects: [
      { id: "p_test", key: "test", name: "Test project" },
      { id: "p_other", key: "other", name: "Other project" },
    ],
  }
}

/**
 * The composer's state is its container's — the route holds it, the dock
 * holds it — so the harness holds it the same way, in the tree rather than in
 * a spy, or the controlled box would not update at all.
 */
function mount(
  roles: Role[] = ["member"],
  projectRoles: Record<string, Role[]> = {},
  seed: SearchTarget | null = null
) {
  const onSend = vi.fn()
  const commands = availableCommands(sessionFor(roles, projectRoles), CUSTOM)

  function Harness() {
    const [value, setValue] = useState("")
    const [seeded, setSeeded] = useState<SearchTarget | null>(seed)
    return (
      <TestSession roles={roles} projectRoles={projectRoles}>
        <ChatComposer
          commands={commands}
          onSend={onSend}
          value={value}
          onValueChange={setValue}
          seed={seeded}
          onSeedChange={setSeeded}
        />
      </TestSession>
    )
  }

  render(<Harness />)

  const box = screen.getByLabelText(
    "Message the console"
  ) as HTMLTextAreaElement
  return { onSend, box }
}

function type(box: HTMLTextAreaElement, value: string) {
  fireEvent.change(box, { target: { value } })
}

describe("the box", () => {
  it("sends on enter and breaks a line on shift-enter", () => {
    const { box, onSend } = mount()

    type(box, "что там с прогоном")
    fireEvent.keyDown(box, { key: "Enter", shiftKey: true })
    expect(onSend).not.toHaveBeenCalled()

    fireEvent.keyDown(box, { key: "Enter" })
    expect(onSend).toHaveBeenCalledWith("что там с прогоном", undefined)
  })

  it("grows rather than crushing a long paste into one line", () => {
    const { box } = mount()

    // The commonest defect in a composer: five lines pasted into a box that
    // stays three high, sent half-read.
    expect(Number(box.getAttribute("rows"))).toBe(3)
    type(box, "one\ntwo\nthree\nfour\nfive\nsix")
    expect(Number(box.getAttribute("rows"))).toBe(6)
  })

  it("stops growing before it eats the thread", () => {
    const { box } = mount()
    type(box, Array.from({ length: 40 }, (_, i) => `line ${i}`).join("\n"))
    expect(Number(box.getAttribute("rows"))).toBe(12)
  })

  it("sends nothing when there is nothing to send", () => {
    const { box, onSend } = mount()
    type(box, "   ")
    fireEvent.keyDown(box, { key: "Enter" })
    expect(onSend).not.toHaveBeenCalled()
  })
})

describe("the slash menu", () => {
  it("offers the client's own commands from data, with their description", () => {
    const { box } = mount()
    type(box, "/rel")

    const options = all("chat-slash-option")
    expect(options).toHaveLength(1)
    expect(options[0]?.getAttribute("data-origin")).toBe("client")
    expect(options[0]?.textContent).toContain("/release")
    expect(options[0]?.textContent).toContain(
      "cut a release branch and open the changelog draft"
    )
  })

  it("mixes the built-in set and the client's in one list", () => {
    const { box } = mount()
    type(box, "/r")
    expect(
      all("chat-slash-option").map((option) => option.textContent)
    ).toEqual([
      expect.stringContaining("/run"),
      expect.stringContaining("/release"),
    ])
  })

  it("closes once an argument is being typed", () => {
    const { box } = mount()
    type(box, "/stop")
    expect(at("chat-slash-menu")).not.toBeNull()
    type(box, "/stop 2a6f1c33")
    expect(at("chat-slash-menu")).toBeNull()
  })

  it("completes on enter instead of sending a half-typed command", () => {
    const { box, onSend } = mount()

    type(box, "/ru")
    fireEvent.keyDown(box, { key: "Enter" })

    expect(onSend).not.toHaveBeenCalled()
    expect(box.value).toBe("/run ")
  })

  it("sends a command that is already whole", () => {
    // Without this, typing a command exactly and pressing enter would add a
    // space and do nothing — which reads as the console ignoring you.
    const { box, onSend } = mount()

    type(box, "/help")
    fireEvent.keyDown(box, { key: "Enter" })

    expect(onSend).toHaveBeenCalledWith("/help", undefined)
  })

  it("walks into the list on arrow-down and back out on escape", () => {
    const { box } = mount()
    type(box, "/r")

    fireEvent.keyDown(box, { key: "ArrowDown" })
    const [first] = all("chat-slash-option")
    expect(document.activeElement).toBe(first)

    fireEvent.keyDown(first as HTMLElement, { key: "Escape" })
    expect(document.activeElement).toBe(box)
    expect(at("chat-slash-menu")).toBeNull()
  })
})

describe("the scope chip", () => {
  it("stays away for a command that does not need a project", () => {
    const { box } = mount()
    type(box, "/help")
    expect(at("chat-scope")).toBeNull()
  })

  it("stays away when the argument already names one", () => {
    // A run id belongs to exactly one project; a chip here would be a second
    // answer that can disagree with the first.
    const { box } = mount()
    type(box, "/stop 2a6f1c33")
    expect(at("chat-scope")).toBeNull()
  })

  it("appears for a command that creates work, and says what it scopes", () => {
    const { box } = mount()
    type(box, "/run PLEX-14")

    expect(at("chat-scope")?.textContent).toContain("/run")
    expect(at("chat-scope")?.textContent).toContain("runs in")
  })

  it("refuses to send until a project is chosen", () => {
    const { box, onSend } = mount()
    type(box, "/run PLEX-14")

    expect(at("chat-incomplete")?.textContent).toBe("/run needs a project")
    fireEvent.keyDown(box, { key: "Enter" })
    expect(onSend).not.toHaveBeenCalled()
  })

  it("offers only the projects where the command's permission holds", () => {
    // `/init` connects a repository, so it answers to `sources.edit` — held
    // here on one project out of two.
    const { box } = mount(["member"], { p_test: ["project-admin"] })
    type(box, "/init")

    const trigger = at("chat-scope-select")
    expect(trigger).not.toBeNull()
    expect(selectValues(trigger as HTMLElement)).toEqual(["p_test"])
  })

  it("refuses, rather than blocks, when there is no project to choose", () => {
    const { box } = mount(["member"])
    type(box, "/init")

    // A permission fact, so the send control is `denied` and names the roles —
    // it stays in the tab order and explains itself.
    expect(at("chat-scope")?.textContent).toContain(
      "needs project-admin or platform-admin"
    )
    expect(at("chat-send")?.getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin"
    )
    expect(at("chat-send")?.hasAttribute("disabled")).toBe(false)
  })

  it("hands the chosen project over when it sends", () => {
    const { box, onSend } = mount(["member"], { p_test: ["project-admin"] })
    type(box, "/run PLEX-14")

    setSelectValue(at("chat-scope-select") as HTMLElement, "p_test")
    fireEvent.keyDown(box, { key: "Enter" })

    expect(onSend).toHaveBeenCalledWith("/run PLEX-14", "p_test")
  })
})

describe("the seeded reference", () => {
  it("shows what the console was opened with, and drops it in one gesture", () => {
    const { box } = mount(["member"], {}, RUN_SEED)

    const chip = at("chat-seed")
    expect(chip?.textContent).toContain("5b1d7e40")
    // The accessible name says which reference it drops — × is not the name
    // of anything.
    expect(chip?.getAttribute("aria-label")).toBe(
      "Drop the reference to 5b1d7e40"
    )

    fireEvent.click(chip as HTMLElement)
    expect(at("chat-seed")).toBeNull()
    // And the box was never touched: a suggestion is not a decision, and an
    // operator typing a general question must not find one started for them.
    expect(box.value).toBe("")
  })

  it("rides along with the next message, spelled once", () => {
    const { box, onSend } = mount(["member"], {}, RUN_SEED)

    type(box, "почему он стоит")
    fireEvent.keyDown(box, { key: "Enter" })

    expect(onSend).toHaveBeenCalledWith("почему он стоит (5b1d7e40)", undefined)
    // Sent — so the suggestion is spent, not carried into the next question.
    expect(at("chat-seed")).toBeNull()
  })

  it("does not repeat an id the operator already spelled", () => {
    const { box, onSend } = mount(["member"], {}, RUN_SEED)

    type(box, "что с 5b1d7e40")
    fireEvent.keyDown(box, { key: "Enter" })

    expect(onSend).toHaveBeenCalledWith("что с 5b1d7e40", undefined)
  })

  it("is absent when the console was opened from nowhere in particular", () => {
    mount()
    expect(at("chat-seed")).toBeNull()
  })
})

describe("recalling the last message", () => {
  /** The harness with a recall offering, the way the console derives one. */
  function mountWithRecall(recall: string | null) {
    const onSend = vi.fn()
    const commands = availableCommands(
      sessionFor(["member"], {}),
      CUSTOM
    )

    function Harness() {
      const [value, setValue] = useState("")
      return (
        <TestSession roles={["member"]}>
          <ChatComposer
            commands={commands}
            onSend={onSend}
            value={value}
            onValueChange={setValue}
            recall={recall}
          />
        </TestSession>
      )
    }

    render(<Harness />)
    return {
      onSend,
      box: screen.getByLabelText(
        "Message the console"
      ) as HTMLTextAreaElement,
    }
  }

  it("offers the last thing said to an empty box on arrow-up", () => {
    const { box } = mountWithRecall("/status")
    fireEvent.keyDown(box, { key: "ArrowUp" })
    expect(box.value).toBe("/status")
  })

  it("does not touch a caret that is moving through a draft", () => {
    // A draft is a thought in progress; arrow-up inside it is editing, not
    // history, and the gesture must not eat what was being written.
    const { box, onSend } = mountWithRecall("/status")
    type(box, "почему очередь")
    fireEvent.keyDown(box, { key: "ArrowUp" })
    expect(box.value).toBe("почему очередь")
    expect(onSend).not.toHaveBeenCalled()
  })

  it("has nothing to offer when nothing was said", () => {
    const { box } = mountWithRecall(null)
    fireEvent.keyDown(box, { key: "ArrowUp" })
    expect(box.value).toBe("")
  })
})
