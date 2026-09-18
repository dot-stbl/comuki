import { describe, expect, it } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import {
  buildSetupFileContents,
  formatSetupSummary,
  isValidApiKeyFormat,
  isValidServerUrl,
  normalizeServerUrl,
  probeHealth,
  SetupApp,
} from "./setup"
import { stripAnsi } from "../theme"

const HEALTHY_KEY = "ck_z6bc48d1" + "a".repeat(43)

function jsonResponse(status: number): Response {
  return new Response(JSON.stringify({ status: "healthy" }), { status })
}

describe("isValidServerUrl", () => {
  it("accepts http and https urls with a host", () => {
    expect(isValidServerUrl("http://localhost:8080")).toBe(true)
    expect(isValidServerUrl("https://comuki.example.io")).toBe(true)
    expect(isValidServerUrl("  https://pad.example.io/  ")).toBe(true)
  })

  it("rejects missing protocol, other protocols and hostless junk", () => {
    expect(isValidServerUrl("localhost:8080")).toBe(false)
    expect(isValidServerUrl("ftp://example.io")).toBe(false)
    expect(isValidServerUrl("http://")).toBe(false)
    expect(isValidServerUrl("")).toBe(false)
    expect(isValidServerUrl("http://not a url")).toBe(false)
  })
})

describe("isValidApiKeyFormat", () => {
  it("accepts a well-formed ck_ token", () => {
    expect(isValidApiKeyFormat(HEALTHY_KEY)).toBe(true)
    expect(isValidApiKeyFormat(`  ${HEALTHY_KEY} `)).toBe(true)
  })

  it("rejects other prefixes, empty bodies and short bodies", () => {
    expect(isValidApiKeyFormat("sk_z6bc48d1aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")).toBe(false)
    expect(isValidApiKeyFormat("ck_")).toBe(false)
    expect(isValidApiKeyFormat("ck_short")).toBe(false)
    expect(isValidApiKeyFormat("")).toBe(false)
  })

  it("rejects characters outside the base64url alphabet", () => {
    expect(isValidApiKeyFormat("ck_abcdefghijklmnop!")).toBe(false)
    expect(isValidApiKeyFormat("ck_abcd efghijklmnop")).toBe(false)
  })
})

describe("normalizeServerUrl", () => {
  it("trims and strips trailing slashes", () => {
    expect(normalizeServerUrl("  https://host.io/// ")).toBe("https://host.io")
  })
})

describe("probeHealth", () => {
  it("returns true for a 2xx /api/v1/health", async () => {
    const seen: string[] = []
    const fetchImpl = async (input: string) => {
      seen.push(input)
      return jsonResponse(200)
    }
    await expect(probeHealth("https://host.io/", fetchImpl)).resolves.toBe(true)
    expect(seen).toEqual(["https://host.io/api/v1/health"])
  })

  it("returns false for a non-2xx health endpoint", async () => {
    const fetchImpl = async () => jsonResponse(503)
    await expect(probeHealth("https://host.io", fetchImpl)).resolves.toBe(false)
  })

  it("returns false when the host is unreachable", async () => {
    const fetchImpl = async () => {
      throw new Error("fetch failed")
    }
    await expect(probeHealth("https://host.io", fetchImpl)).resolves.toBe(false)
  })
})

describe("buildSetupFileContents", () => {
  it("merges the draft over the existing file without losing unknown keys", () => {
    expect(
      buildSetupFileContents(
        { tenant: "acme", bell: false, defaultProject: "old" },
        {
          url: "https://host.io",
          cookie: "Comuki.Session=abc",
          defaultProject: "nova",
          theme: "graphite-dark",
        }
      )
    ).toEqual({
      tenant: "acme",
      bell: false,
      url: "https://host.io",
      cookie: "Comuki.Session=abc",
      defaultProject: "nova",
      theme: "graphite-dark",
    })
  })

  it("keeps existing credentials when the draft skips auth", () => {
    expect(
      buildSetupFileContents(
        { apiKey: "ck_existing", url: "https://old.io" },
        { url: "https://host.io", theme: "dichromat-dark" }
      )
    ).toEqual({
      apiKey: "ck_existing",
      url: "https://host.io",
      theme: "dichromat-dark",
    })
  })
})

describe("formatSetupSummary", () => {
  it("masks the api key and never prints it in full", () => {
    const text = stripAnsi(
      formatSetupSummary(
        {
          url: "https://host.io",
          apiKey: HEALTHY_KEY,
          defaultProject: "nova",
          theme: "graphite-dark",
        },
        "~/.config/comuki/config.json"
      )
    )
    expect(text).toContain("setup complete")
    expect(text).toContain("api-key ck_z6bc4…")
    expect(text).not.toContain(HEALTHY_KEY)
    expect(text).toContain("project  nova")
    expect(text).toContain("theme    graphite-dark")
  })

  it("masks the cookie to its name", () => {
    const text = stripAnsi(
      formatSetupSummary(
        {
          url: "https://host.io",
          cookie: "Comuki.Session=super-secret-value",
          theme: "dichromat-light",
        },
        "~/.config/comuki/config.json"
      )
    )
    expect(text).toContain("auth     cookie Comuki.Session=***")
    expect(text).not.toContain("super-secret-value")
    expect(text).toContain("project  —")
  })

  it("reports anonymous when the wizard skipped auth", () => {
    const text = stripAnsi(
      formatSetupSummary(
        { url: "https://host.io", theme: "bureau-dark" },
        "~/.config/comuki/config.json"
      )
    )
    expect(text).toContain("auth     anonymous")
  })
})

describe("SetupApp — first frame", () => {
  it("opens on the url step with the esc hint", async () => {
    const fetchImpl = async () => new Response("{}", { status: 200 })
    const { lastFrame, unmount } = render(<SetupApp fetchImpl={fetchImpl} />)
    await new Promise((resolve) => setTimeout(resolve, 80))
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("comuki setup")
    expect(frame).toContain("esc skips")
    expect(frame).toMatch(/url\s+>/)
    expect(frame).not.toContain("setup skipped")
    unmount()
  })
})
