import { describe, expect, it } from "vitest"

import {
  BUILT_IN_COMMANDS,
  availableCommands,
  commandMenuQuery,
  commandOf,
  matchCommands,
  scopeChoices,
  scopeState,
} from "@/domains/chat/model/commands"
import type { SlashCommand } from "@/domains/chat/model/types"
import type { Role, Session } from "@/shared/session"

/**
 * The scope rule, which is the one adaptation this screen makes to §7.
 *
 * §7 asked for a project chip and meant a mode; there is no current project in
 * this product any more, so the chip became an explicit scope on the commands
 * that cannot proceed without one. These cases are that line, asserted — and
 * the case that matters most is the last one: the chip is a courtesy, and the
 * gate is on the proposal.
 */

function session(
  platformRoles: Role[],
  projectRoles: Record<string, Role[]> = {}
): Session {
  return {
    user: {
      id: "u_test",
      name: "Test",
      email: "test@comuki.local",
      platformRoles,
      projectRoles,
    },
    projects: [
      { id: "p_one", key: "one", name: "One" },
      { id: "p_two", key: "two", name: "Two" },
      { id: "p_three", key: "three", name: "Three" },
    ],
  }
}

const CUSTOM: SlashCommand[] = [
  {
    name: "/release",
    description: "cut a release branch",
    origin: "client",
    scope: "implied",
    permission: "inbox.take",
    projectId: "p_one",
  },
  {
    name: "/elsewhere",
    description: "declared in a project this session cannot see",
    origin: "client",
    scope: "implied",
    permission: "inbox.take",
    projectId: "p_hidden",
  },
]

describe("which commands the composer offers", () => {
  it("puts the client's own commands beside the built-in set", () => {
    const offered = availableCommands(session(["member"]), CUSTOM)

    // Custom commands are *data*. Nothing in the domain knows their names.
    expect(offered.map((entry) => entry.name)).toContain("/release")
    expect(offered.find((entry) => entry.name === "/release")).toMatchObject({
      origin: "client",
      description: "cut a release branch",
      projectId: "p_one",
    })
  })

  it("drops a command declared in a project the session cannot see", () => {
    const offered = availableCommands(session(["member"]), CUSTOM)
    expect(offered.map((entry) => entry.name)).not.toContain("/elsewhere")
  })

  it("offers every built-in command §7 names", () => {
    expect(BUILT_IN_COMMANDS.map((entry) => entry.name).sort()).toEqual(
      [
        "/debug",
        "/help",
        "/init",
        "/plan",
        "/project",
        "/run",
        "/status",
        "/stop",
      ].sort()
    )
  })
})

describe("the menu opens on a command and closes on an argument", () => {
  it("is open while the first token is a slash word", () => {
    expect(commandMenuQuery("/ru")).toBe("/ru")
    expect(matchCommands("/ru", BUILT_IN_COMMANDS).map((c) => c.name)).toEqual([
      "/run",
    ])
  })

  it("closes the moment an argument is being typed", () => {
    // A menu over an argument is a menu in the way.
    expect(commandMenuQuery("/run PLEX-14")).toBeNull()
    expect(commandMenuQuery("what is running")).toBeNull()
  })

  it("still knows which command a typed line is", () => {
    expect(commandOf("/stop 2a6f1c33", BUILT_IN_COMMANDS)?.name).toBe("/stop")
    expect(commandOf("just words", BUILT_IN_COMMANDS)).toBeNull()
    expect(commandOf("/nope arg", BUILT_IN_COMMANDS)).toBeNull()
  })
})

describe("where the line is drawn on the scope chip", () => {
  const shift = session(["member"], { p_one: ["project-admin"] })

  it("asks for nothing when the command is not about a project", () => {
    for (const name of ["/help", "/status", "/project"]) {
      const command = commandOf(name, BUILT_IN_COMMANDS)
      expect(scopeState(shift, command, "").needed).toBe(false)
    }
  })

  it("asks for nothing when the argument already names one", () => {
    // A run id belongs to exactly one project. Asking again would produce a
    // second answer that can disagree with the first.
    for (const line of ["/stop 2a6f1c33", "/plan 5b1d7e40"]) {
      const command = commandOf(line, BUILT_IN_COMMANDS)
      expect(command?.scope).toBe("implied")
      expect(scopeState(shift, command, "").needed).toBe(false)
    }
  })

  it("requires one for the two commands that create something", () => {
    for (const name of ["/run", "/init"]) {
      const command = commandOf(name, BUILT_IN_COMMANDS)
      const state = scopeState(shift, command, "")
      expect(state.needed).toBe(true)
      expect(state.incomplete).toBe(`${name} needs a project`)
    }
  })

  it("stops blocking once a project is chosen", () => {
    const command = commandOf("/run", BUILT_IN_COMMANDS)
    expect(scopeState(shift, command, "p_one").incomplete).toBeNull()
  })

  it("offers only the projects where the command's permission holds", () => {
    // `/init` connects a repository, so it answers to `sources.edit` — which
    // this shift holds on one project out of three.
    const command = commandOf("/init", BUILT_IN_COMMANDS)
    expect(scopeChoices(shift, command).map((p) => p.key)).toEqual(["one"])
  })

  it("refuses rather than blocks when there is no project to choose", () => {
    // A permission fact, so `denied` and a sentence naming the roles — not
    // `incomplete`, which is what an unfinished command gets.
    const watcher = session(["viewer"])
    const command = commandOf("/init", BUILT_IN_COMMANDS)
    const state = scopeState(watcher, command, "")

    expect(state.choices).toEqual([])
    expect(state.incomplete).toBeNull()
    expect(state.denied).toBe("needs project-admin or platform-admin")
  })

  it("cannot filter what was never chosen — which is why the gate is elsewhere", () => {
    // The chip's filter is a courtesy on `required` commands. `/plan` is
    // `implied`, so its project comes from the run id and the chip never gets
    // a say; the refusal has to happen on the proposal's own control.
    const command = commandOf("/plan 2a6f1c33", BUILT_IN_COMMANDS)
    expect(scopeState(shift, command, "").needed).toBe(false)
    expect(scopeChoices(shift, command)).toEqual([])
  })
})
