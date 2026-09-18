import { describe, expect, it } from "bun:test"
import { stripAnsi } from "../theme"
import {
  collectDoctorChecks,
  doctorFailed,
  formatDoctorReport,
  probeDoctorHealth,
  type DoctorCheck,
  type DoctorFetch,
} from "./doctor"
import type { MeView } from "../lib/client"

function jsonResponse(status: number, body: unknown = {}): Response {
  return new Response(JSON.stringify(body), { status })
}

const ME: MeView = {
  userId: "u1",
  subjectType: "user",
  subjectId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  email: "ada@example.io",
  displayName: "Ada",
  roles: ["owner"],
  permissions: ["chat:write"],
}

function routedFetch(routes: Record<string, Response | Error>): DoctorFetch {
  return async (input) => {
    const url = String(input)
    for (const [needle, result] of Object.entries(routes)) {
      if (url.includes(needle)) {
        if (result instanceof Error) {
          throw result
        }
        return result
      }
    }
    return jsonResponse(404)
  }
}

describe("probeDoctorHealth", () => {
  it("returns true for a 2xx /api/v1/health", async () => {
    const seen: string[] = []
    const fetchImpl: DoctorFetch = async (input) => {
      seen.push(String(input))
      return jsonResponse(200)
    }
    await expect(
      probeDoctorHealth("https://host.io/", fetchImpl)
    ).resolves.toBe(true)
    expect(seen).toEqual(["https://host.io/api/v1/health"])
  })

  it("returns false for a non-2xx or unreachable host", async () => {
    await expect(
      probeDoctorHealth("https://host.io", async () => jsonResponse(503))
    ).resolves.toBe(false)
    await expect(
      probeDoctorHealth("https://host.io", async () => {
        throw new Error("fetch failed")
      })
    ).resolves.toBe(false)
  })
})

describe("formatDoctorReport", () => {
  it("aligns ok/fail in one column", () => {
    const checks: readonly DoctorCheck[] = [
      { name: "url", ok: true, detail: "https://host.io" },
      { name: "auth", ok: false, detail: "unauthorized" },
      { name: "config", ok: true, detail: "~/.config/comuki/config.json" },
      { name: "theme", ok: true, detail: "dichromat-dark (default)" },
    ]
    const lines = formatDoctorReport(checks).split("\n").map(stripAnsi)
    expect(lines[0]).toBe("url     ok    https://host.io")
    expect(lines[1]).toBe("auth    fail  unauthorized")
    expect(lines[2]).toBe("config  ok    ~/.config/comuki/config.json")
    expect(lines[3]).toBe("theme   ok    dichromat-dark (default)")
  })
})

describe("collectDoctorChecks", () => {
  const home = "/home/tester"
  const configPath = "/home/tester/.config/comuki/config.json"

  it("reports all four checks ok on a healthy host", async () => {
    const checks = await collectDoctorChecks({
      overrides: { url: "https://host.io" },
      env: {},
      file: { theme: "dockside-dark" },
      configPath,
      configExists: true,
      fetchImpl: routedFetch({
        "/api/v1/health": jsonResponse(200),
        "/api/v1/auth/me": jsonResponse(200, ME),
      }),
      home,
    })
    expect(doctorFailed(checks)).toBe(false)
    expect(checks).toEqual([
      { name: "url", ok: true, detail: "https://host.io" },
      { name: "auth", ok: true, detail: "user · Ada" },
      { name: "config", ok: true, detail: "~/.config/comuki/config.json" },
      { name: "theme", ok: true, detail: "dockside-dark" },
    ])
  })

  it("fails url and skips auth when the host is missing", async () => {
    const checks = await collectDoctorChecks({
      overrides: {},
      env: {},
      file: {},
      configPath,
      configExists: false,
      fetchImpl: routedFetch({}),
      home,
    })
    expect(doctorFailed(checks)).toBe(true)
    expect(checks.find((check) => check.name === "url")).toEqual({
      name: "url",
      ok: false,
      detail: "missing — pass --url or set COMUKI_URL",
    })
    expect(checks.find((check) => check.name === "auth")).toEqual({
      name: "auth",
      ok: false,
      detail: "skipped (no url)",
    })
    expect(checks.find((check) => check.name === "config")?.ok).toBe(false)
  })

  it("falls back to the file url when arg and env are unset", async () => {
    const checks = await collectDoctorChecks({
      overrides: {},
      env: {},
      file: { url: "https://from-file.io///" },
      configPath,
      configExists: true,
      fetchImpl: routedFetch({
        "/api/v1/health": jsonResponse(200),
        "/api/v1/auth/me": jsonResponse(401),
      }),
      home,
    })
    expect(checks.find((check) => check.name === "url")).toEqual({
      name: "url",
      ok: true,
      detail: "https://from-file.io",
    })
    expect(checks.find((check) => check.name === "auth")).toEqual({
      name: "auth",
      ok: false,
      detail: "unauthorized",
    })
  })

  it("fails an unknown theme and an unreachable host", async () => {
    const checks = await collectDoctorChecks({
      overrides: { url: "https://host.io", theme: "not-a-theme" },
      env: {},
      file: {},
      configPath,
      configExists: true,
      fetchImpl: routedFetch({
        "/api/v1/health": jsonResponse(503),
      }),
      home,
    })
    expect(checks.find((check) => check.name === "url")?.ok).toBe(false)
    expect(checks.find((check) => check.name === "theme")).toEqual({
      name: "theme",
      ok: false,
      detail: "unknown: not-a-theme",
    })
  })
})
