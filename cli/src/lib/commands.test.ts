import { describe, expect, it } from "bun:test"
import { resolveArchiveAction, resolveCommand } from "./commands"

describe("resolveCommand", () => {
  it("routes bare comuki to the repl", () => {
    expect(resolveCommand([])).toBe("repl")
  })

  it("routes the known subcommands", () => {
    expect(resolveCommand(["status"])).toBe("status")
    expect(resolveCommand(["runs"])).toBe("runs")
    expect(resolveCommand(["runs", "list"])).toBe("runs")
    expect(resolveCommand(["login"])).toBe("login")
    expect(resolveCommand(["whoami"])).toBe("whoami")
    expect(resolveCommand(["config"])).toBe("config")
    expect(resolveCommand(["config", "show"])).toBe("config")
    expect(resolveCommand(["setup"])).toBe("setup")
    expect(resolveCommand(["completion"])).toBe("completion")
    expect(resolveCommand(["completion", "pwsh"])).toBe("completion")
    expect(resolveCommand(["doctor"])).toBe("doctor")
    expect(resolveCommand(["archive"])).toBe("archive")
  })

  it("treats the removed chat subcommand as unknown", () => {
    expect(resolveCommand(["chat"])).toBe("unknown")
  })

  it("flags anything else unknown", () => {
    expect(resolveCommand(["bogus"])).toBe("unknown")
  })
})

describe("resolveArchiveAction", () => {
  it("bare [archive] is list", () => {
    expect(resolveArchiveAction(["archive"], false)).toEqual({ kind: "list" })
  })

  it("explicit `list` is list", () => {
    expect(resolveArchiveAction(["archive", "list"], false)).toEqual({
      kind: "list",
    })
  })

  it("`save <session>` with current=false keeps the explicit id", () => {
    expect(resolveArchiveAction(["archive", "save", "s1"], false)).toEqual({
      kind: "save",
      sessionId: "s1",
      current: false,
    })
  })

  it("`save` with current=true leaves sessionId undefined", () => {
    expect(resolveArchiveAction(["archive", "save"], true)).toEqual({
      kind: "save",
      sessionId: undefined,
      current: true,
    })
  })

  it("unknown action surfaces its name", () => {
    expect(resolveArchiveAction(["archive", "bogus"], false)).toEqual({
      kind: "unknown",
      action: "bogus",
    })
  })
})
