import { describe, expect, it } from "bun:test"
import {
  SLASH_COMMANDS,
  resolveSlashAction,
  slashHelpLines,
} from "./slash"
import { stripAnsi } from "../theme"

describe("resolveSlashAction", () => {
  it("routes exit and its aliases, with or without the slash", () => {
    expect(resolveSlashAction("/exit")).toEqual({ kind: "exit" })
    expect(resolveSlashAction("/quit")).toEqual({ kind: "exit" })
    expect(resolveSlashAction("/q")).toEqual({ kind: "exit" })
    expect(resolveSlashAction("exit")).toEqual({ kind: "exit" })
  })

  it("routes every registered command by name (case-insensitive)", () => {
    expect(resolveSlashAction("/retry")).toEqual({ kind: "retry" })
    expect(resolveSlashAction("/RETRY")).toEqual({ kind: "retry" })
    expect(resolveSlashAction("/clear")).toEqual({ kind: "clear" })
    expect(resolveSlashAction("/help")).toEqual({ kind: "help" })
    expect(resolveSlashAction("/sessions")).toEqual({ kind: "sessions" })
    expect(resolveSlashAction("/new")).toEqual({ kind: "new" })
    expect(resolveSlashAction("approve")).toEqual({ kind: "approve" })
    expect(resolveSlashAction("reject")).toEqual({ kind: "reject" })
  })

  it("keeps rename title case and collapses whitespace", () => {
    expect(resolveSlashAction("/rename  Fix   the Readme")).toEqual({
      kind: "rename",
      title: "Fix the Readme",
    })
    expect(resolveSlashAction("rename Readme pass")).toEqual({
      kind: "rename",
      title: "Readme pass",
    })
  })

  it("rename without arguments resolves with an empty title", () => {
    expect(resolveSlashAction("/rename")).toEqual({
      kind: "rename",
      title: "",
    })
    expect(resolveSlashAction("/rename    ")).toEqual({
      kind: "rename",
      title: "",
    })
  })

  it("carries the reject reason, slash-prefixed or bare", () => {
    expect(resolveSlashAction("/reject too risky")).toEqual({
      kind: "reject",
      reason: "too risky",
    })
    expect(resolveSlashAction("reject too risky")).toEqual({
      kind: "reject",
      reason: "too risky",
    })
  })

  it("treats unknown or empty input as a chat message", () => {
    expect(resolveSlashAction("hello world")).toEqual({ kind: "message" })
    expect(resolveSlashAction("/bogus")).toEqual({ kind: "message" })
    expect(resolveSlashAction("/")).toEqual({ kind: "message" })
  })
})

describe("slashHelpLines", () => {
  it("lists every registered command with its description", () => {
    const help = slashHelpLines().map(stripAnsi)
    for (const command of SLASH_COMMANDS) {
      expect(
        help.some((line) => line.trimStart().startsWith(command.usage))
      ).toBe(true)
      expect(help.some((line) => line.includes(command.description))).toBe(
        true
      )
    }
  })

  it("shows the new commands so /help stays registry-driven", () => {
    const help = slashHelpLines().map(stripAnsi).join("\n")
    expect(help).toContain("/retry")
    expect(help).toContain("/rename <title>")
  })

  it("aligns descriptions in one column", () => {
    const rows = slashHelpLines().map(stripAnsi).slice(1)
    const starts = rows.map(
      (line, index) => line.indexOf(SLASH_COMMANDS[index].description)
    )
    expect(new Set(starts).size).toBe(1)
  })
})
