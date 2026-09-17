import { describe, expect, it } from "bun:test"
import { resolveCommand } from "./commands"

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
  })

  it("treats the removed chat subcommand as unknown", () => {
    expect(resolveCommand(["chat"])).toBe("unknown")
  })

  it("flags anything else unknown", () => {
    expect(resolveCommand(["bogus"])).toBe("unknown")
  })
})
