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

  it("parses /bell on|off; bare or unparsable reads as a status query", () => {
    expect(resolveSlashAction("/bell on")).toEqual({
      kind: "bell",
      enabled: true,
    })
    expect(resolveSlashAction("/bell off")).toEqual({
      kind: "bell",
      enabled: false,
    })
    expect(resolveSlashAction("/bell")).toEqual({ kind: "bell" })
    expect(resolveSlashAction("/bell maybe")).toEqual({ kind: "bell" })
  })

  it("carries the export path, slash-prefixed or bare", () => {
    expect(resolveSlashAction("/export")).toEqual({ kind: "export" })
    expect(resolveSlashAction("/export notes.md")).toEqual({
      kind: "export",
      path: "notes.md",
    })
    expect(resolveSlashAction("export ./out/notes.md")).toEqual({
      kind: "export",
      path: "./out/notes.md",
    })
  })

  it("treats unknown or empty input as a chat message", () => {
    expect(resolveSlashAction("hello world")).toEqual({ kind: "message" })
    expect(resolveSlashAction("/bogus")).toEqual({ kind: "message" })
    expect(resolveSlashAction("/")).toEqual({ kind: "message" })
  })

  it("routes the ops pack: /runs, /plan, /project", () => {
    expect(resolveSlashAction("/runs")).toEqual({ kind: "runs" })
    expect(resolveSlashAction("runs")).toEqual({ kind: "runs" })
    expect(resolveSlashAction("/plan")).toEqual({ kind: "plan" })
    expect(resolveSlashAction("/project nova")).toEqual({
      kind: "project",
      query: "nova",
    })
  })

  it("resolves /project without arguments to an empty query", () => {
    expect(resolveSlashAction("/project")).toEqual({ kind: "project", query: "" })
    expect(resolveSlashAction("/project   ")).toEqual({
      kind: "project",
      query: "",
    })
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
    expect(help).toContain("/runs")
    expect(help).toContain("/plan")
    expect(help).toContain("/project <id|slug|name>")
  })

  it("aligns descriptions in one column", () => {
    const rows = slashHelpLines().map(stripAnsi).slice(1)
    const starts = rows.map(
      (line, index) => line.indexOf(SLASH_COMMANDS[index].description)
    )
    expect(new Set(starts).size).toBe(1)
  })
})
