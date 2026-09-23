// scripts/lib/ui-probe-args.test.ts
//
// Unit tests for the pure `ui:probe` argument parser (WS17). Run with
// Bun's own test runner, scoped to this directory —
// `bun test scripts/lib` — NOT `bun run test` (that's `vitest run`,
// configured to discover only `src/**/*.test.{ts,tsx}`; scripts/ is
// tooling, not app code, and stays out of that glob on purpose). See
// scripts/UI-PROBE.md "Testing the pure parts" for why and how this is
// wired.

import { describe, expect, it } from "bun:test"

import {
  assertPoolPort,
  DEFAULT_THEME,
  DEFAULT_TIMEOUT_MS,
  DEFAULT_VIEWPORT,
  parseArgs,
  parseTheme,
  parseViewport,
  PORT_POOL_MAX,
  PORT_POOL_MIN,
  PortRangeError,
  PortReservedError,
  RESERVED_PORT,
  slugifyTarget,
  UiProbeArgError,
} from "./ui-probe-args"

describe("parseTheme", () => {
  it("maps light/dark to a single-element list", () => {
    expect(parseTheme("light")).toEqual(["light"])
    expect(parseTheme("dark")).toEqual(["dark"])
  })

  it("maps both to light and dark", () => {
    expect(parseTheme("both")).toEqual(["light", "dark"])
  })

  it("rejects anything else", () => {
    expect(() => parseTheme("blue")).toThrow(UiProbeArgError)
  })
})

describe("parseViewport", () => {
  it("parses WxH", () => {
    expect(parseViewport("1440x900")).toEqual({ width: 1440, height: 900 })
  })

  it("is case-insensitive on the separator", () => {
    expect(parseViewport("1280X720")).toEqual({ width: 1280, height: 720 })
  })

  it("rejects malformed input", () => {
    expect(() => parseViewport("1440")).toThrow(UiProbeArgError)
    expect(() => parseViewport("1440x")).toThrow(UiProbeArgError)
    expect(() => parseViewport("x900")).toThrow(UiProbeArgError)
    expect(() => parseViewport("not-a-viewport")).toThrow(UiProbeArgError)
  })

  it("rejects zero or negative dimensions", () => {
    expect(() => parseViewport("0x900")).toThrow(UiProbeArgError)
  })
})

describe("assertPoolPort", () => {
  it("accepts a port inside the band, excluding the reserved one", () => {
    expect(() => assertPoolPort(PORT_POOL_MIN)).not.toThrow()
    expect(() => assertPoolPort(PORT_POOL_MAX)).not.toThrow()
  })

  it("rejects a port outside the band", () => {
    expect(() => assertPoolPort(PORT_POOL_MIN - 1)).toThrow(PortRangeError)
    expect(() => assertPoolPort(PORT_POOL_MAX + 1)).toThrow(PortRangeError)
    expect(() => assertPoolPort(17010)).toThrow(PortRangeError)
  })

  it("rejects test:storybook's reserved port even though it is in-band", () => {
    expect(() => assertPoolPort(RESERVED_PORT)).toThrow(PortReservedError)
  })
})

describe("slugifyTarget", () => {
  it("replaces non-alphanumeric runs with underscores", () => {
    expect(slugifyTarget("runs-list--default")).toBe("runs-list--default")
    expect(slugifyTarget("/runs/123")).toBe("runs_123")
  })

  it("trims leading/trailing underscores", () => {
    expect(slugifyTarget("/runs")).toBe("runs")
  })

  it("falls back to a stable name for an empty slug", () => {
    expect(slugifyTarget("/")).toBe("root")
  })
})

describe("parseArgs", () => {
  it("accepts --story alone with every other flag defaulted", () => {
    const options = parseArgs(["--story", "runs-list--default"])
    expect(options).toEqual({
      mode: "story",
      target: "runs-list--default",
      themes: [DEFAULT_THEME],
      viewport: DEFAULT_VIEWPORT,
      build: true,
      out: null,
      port: null,
      timeoutMs: DEFAULT_TIMEOUT_MS,
      failOnConsoleError: false,
      failOnAxe: false,
    })
  })

  it("accepts --page alone", () => {
    const options = parseArgs(["--page", "/runs"])
    expect(options.mode).toBe("page")
    expect(options.target).toBe("/runs")
  })

  it("accepts both `--flag value` and `--flag=value` forms", () => {
    const spaceForm = parseArgs(["--story", "a--b", "--theme", "both"])
    const equalsForm = parseArgs(["--story=a--b", "--theme=both"])
    expect(spaceForm.target).toBe("a--b")
    expect(spaceForm.themes).toEqual(["light", "dark"])
    expect(equalsForm).toEqual(spaceForm)
  })

  it("parses --viewport, --out, --port, --timeout", () => {
    const options = parseArgs([
      "--story",
      "a--b",
      "--viewport",
      "1280x720",
      "--out",
      "artifacts/tmp",
      "--port",
      "17190",
      "--timeout",
      "5000",
    ])
    expect(options.viewport).toEqual({ width: 1280, height: 720 })
    expect(options.out).toBe("artifacts/tmp")
    expect(options.port).toBe(17190)
    expect(options.timeoutMs).toBe(5000)
  })

  it("--skip-build turns off the default build", () => {
    const options = parseArgs(["--story", "a--b", "--skip-build"])
    expect(options.build).toBe(false)
  })

  it("--build is the (already-default) explicit form", () => {
    const options = parseArgs(["--story", "a--b", "--build"])
    expect(options.build).toBe(true)
  })

  it("--fail-on-console-error and --fail-on-axe are opt-in", () => {
    const options = parseArgs(["--story", "a--b", "--fail-on-console-error", "--fail-on-axe"])
    expect(options.failOnConsoleError).toBe(true)
    expect(options.failOnAxe).toBe(true)
  })

  it("rejects neither --story nor --page", () => {
    expect(() => parseArgs([])).toThrow(UiProbeArgError)
  })

  it("rejects both --story and --page", () => {
    expect(() => parseArgs(["--story", "a--b", "--page", "/runs"])).toThrow(UiProbeArgError)
  })

  it("rejects an out-of-band --port", () => {
    expect(() => parseArgs(["--story", "a--b", "--port", "5173"])).toThrow(PortRangeError)
  })

  it("rejects the reserved --port", () => {
    expect(() => parseArgs(["--story", "a--b", "--port", String(RESERVED_PORT)])).toThrow(PortReservedError)
  })

  it("rejects an unrecognised flag", () => {
    expect(() => parseArgs(["--story", "a--b", "--nonsense"])).toThrow(UiProbeArgError)
  })

  it("rejects a flag missing its value", () => {
    expect(() => parseArgs(["--story"])).toThrow(UiProbeArgError)
    expect(() => parseArgs(["--story", "--theme", "dark"])).toThrow(UiProbeArgError)
  })
})
