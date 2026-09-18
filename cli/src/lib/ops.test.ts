import { describe, expect, it } from "bun:test"
import type { ChildProcess } from "node:child_process"
import { PENDING_PREFIX } from "./sessions"
import { linkSequence } from "./term"
import {
  DASHBOARD_CHAT_PATH,
  DASHBOARD_RUNS_PATH,
  dashboardLinkLine,
  dashboardOpenUrl,
  noteUnavailableLines,
  noteUsageLines,
  openDashboardUrl,
  openPanelLines,
  openerCommand,
  toolsUnavailableLines,
} from "./ops"
import { stripAnsi } from "../theme"

describe("dashboardOpenUrl", () => {
  it("a live session lands on /chat — there is no /chat/{id} route", () => {
    expect(
      dashboardOpenUrl("http://h:17173/", "018f1a2b-3c4d-7e5f-8a9b-0c1d2e3f4a5b")
    ).toBe(`http://h:17173${DASHBOARD_CHAT_PATH}`)
  })

  it("strips a trailing slash on the host once", () => {
    expect(dashboardOpenUrl("http://h:17173", "abc")).toBe(
      `http://h:17173${DASHBOARD_CHAT_PATH}`
    )
  })

  it("a pending local- tab has no server counterpart — falls through to /runs", () => {
    expect(
      dashboardOpenUrl("http://h:17173", `${PENDING_PREFIX}1-0`)
    ).toBe(`http://h:17173${DASHBOARD_RUNS_PATH}`)
  })

  it("no session id opens the runs ledger", () => {
    expect(dashboardOpenUrl("http://h:17173", undefined)).toBe(
      `http://h:17173${DASHBOARD_RUNS_PATH}`
    )
    expect(dashboardOpenUrl("http://h:17173", "")).toBe(
      `http://h:17173${DASHBOARD_RUNS_PATH}`
    )
  })
})

describe("dashboardLinkLine", () => {
  it("wraps the url in OSC-8 so a supporting terminal is clickable", () => {
    const url = "http://h:17173/chat"
    expect(dashboardLinkLine(url)).toContain(linkSequence(url, url))
  })

  it("stripping OSC-8 leaves the bare url — the honest fallback", () => {
    const url = "http://h:17173/runs"
    const stripped = stripAnsi(dashboardLinkLine(url))
    expect(stripped).toContain(url)
  })
})

describe("openPanelLines", () => {
  it("chat reason names the missing /chat/{id} route", () => {
    const lines = openPanelLines("http://h/chat", true, "chat").map(stripAnsi)
    expect(lines[0]).toContain("open")
    expect(lines[1]).toContain("http://h/chat")
    expect(lines[2]).toContain("no /chat/{id}")
    expect(lines[3]).toContain("opened in the system browser")
  })

  it("runs reason + no opener says to copy the url", () => {
    const lines = openPanelLines("http://h/runs", false, "runs").map(stripAnsi)
    expect(lines[2]).toContain("no live session")
    expect(lines[3]).toContain("no system opener")
    expect(lines[3]).toContain("copy the url")
  })
})

describe("toolsUnavailableLines", () => {
  it("the honest notice names the missing GET and where tools actually live", () => {
    const lines = toolsUnavailableLines().map(stripAnsi)
    expect(lines[0]).toContain("no HTTP GET for brain tools / MCP tools")
    expect(lines[1]).toContain("per-request")
    expect(lines[2]).toContain("POST /api/v1/mcp")
  })
})

describe("noteUnavailableLines", () => {
  it("says memory write is MCP-only — there is no HTTP POST for notes", () => {
    const lines = noteUnavailableLines().map(stripAnsi)
    expect(lines[0]).toContain("memory write is MCP-only")
    expect(lines[1]).toContain("memory.note")
    expect(lines[1]).toContain("no HTTP POST")
  })

  it("/note without text shows usage then the same notice", () => {
    const lines = noteUsageLines().map(stripAnsi)
    expect(lines[0]).toContain("usage: /note <text>")
    expect(lines[1]).toContain("memory write is MCP-only")
  })
})

describe("openerCommand", () => {
  it("picks start/cmd on Windows, open on macOS, xdg-open on Linux", () => {
    expect(openerCommand("win32")).toBe("cmd")
    expect(openerCommand("darwin")).toBe("open")
    expect(openerCommand("linux")).toBe("xdg-open")
  })

  it("unknown platforms have no opener", () => {
    expect(openerCommand("freebsd")).toBeNull()
  })
})

describe("openDashboardUrl", () => {
  it("spawns xdg-open with the url on linux and reports success", () => {
    const seen: { command: string; args: readonly string[] }[] = []
    const ok = openDashboardUrl("http://h/chat", {
      platform: "linux",
      spawnImpl: ((command: string, args: readonly string[]) => {
        seen.push({ command, args })
        return { unref() {} } as ChildProcess
      }) as typeof import("node:child_process").spawn,
    })
    expect(ok).toBe(true)
    expect(seen).toEqual([{ command: "xdg-open", args: ["http://h/chat"] }])
  })

  it("windows goes through cmd /c start so a quoted url is not a window title", () => {
    const seen: { command: string; args: readonly string[] }[] = []
    const ok = openDashboardUrl("http://h/runs", {
      platform: "win32",
      spawnImpl: ((command: string, args: readonly string[]) => {
        seen.push({ command, args })
        return { unref() {} } as ChildProcess
      }) as typeof import("node:child_process").spawn,
    })
    expect(ok).toBe(true)
    expect(seen[0]?.command).toBe("cmd")
    expect(seen[0]?.args).toEqual(["/c", "start", "", "http://h/runs"])
  })

  it("no opener (or a throwing spawn) reports false — the panel still prints the url", () => {
    expect(
      openDashboardUrl("http://h/chat", { platform: "freebsd" })
    ).toBe(false)
    expect(
      openDashboardUrl("http://h/chat", {
        platform: "linux",
        spawnImpl: (() => {
          throw new Error("ENOENT")
        }) as unknown as typeof import("node:child_process").spawn,
      })
    ).toBe(false)
  })
})
