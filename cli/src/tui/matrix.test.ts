/**
 * Terminal compatibility matrix end-to-end test (issue #79).
 *
 * Synthesises `process.argv`, `process.env`, `process.stdout.isTTY`
 * for each row of the matrix table the issue calls out and asserts
 * the resolved `ResolvedModes` matches the expected outcome.
 *
 * Unlike `modes.test.ts` (which asserts the resolver in isolation),
 * this file walks the matrix the issue spec mandates, including
 * cross-flag dominance. The matrix is the canonical contract; if a
 * row disagrees with the resolver, the resolver is wrong.
 */
import { describe, expect, test } from "bun:test"
import { resolveModes, type Args } from "./modes"
import type { CapabilityDetect } from "./modes"

/**
 * A single matrix row — input (args, env, detect) → expected flags.
 */
interface MatrixRow {
  readonly title: string
  readonly args: Args
  readonly env: Record<string, string | undefined>
  readonly detect: CapabilityDetect
  readonly expect: {
    readonly linear: boolean
    readonly machine: boolean
    readonly noColor: boolean
    readonly ascii: boolean
    readonly noMouse: boolean
  }
}

const TTY: CapabilityDetect = { stdoutIsTTY: true, stdinIsTTY: true, columns: 80, rows: 24 }
const PIPE: CapabilityDetect = { stdoutIsTTY: false, stdinIsTTY: false, columns: 80, rows: 24 }

const MATRIX: readonly MatrixRow[] = [
  {
    title: "TTY, xterm-256color → full OpenTUI",
    args: {},
    env: { TERM: "xterm-256color", COLORTERM: "truecolor" },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: false, ascii: false, noMouse: false },
  },
  {
    title: "TTY, TERM=dumb → --no-color + ascii",
    args: {},
    env: { TERM: "dumb" },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: true, ascii: true, noMouse: true },
  },
  {
    title: "TTY, TERM=linux → --no-color + ascii",
    args: {},
    env: { TERM: "linux" },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: true, ascii: true, noMouse: true },
  },
  {
    title: "Not a TTY (pipe) → --machine",
    args: {},
    env: { TERM: "xterm-256color", COLORTERM: "truecolor" },
    detect: PIPE,
    expect: { linear: false, machine: true, noColor: true, ascii: false, noMouse: true },
  },
  {
    title: "Screen reader (ACCESSIBILITY_ENABLED=1) → --mode linear",
    args: {},
    env: {
      TERM: "xterm-256color",
      COLORTERM: "truecolor",
      ACCESSIBILITY_ENABLED: "1",
    },
    detect: TTY,
    expect: { linear: true, machine: false, noColor: false, ascii: false, noMouse: false },
  },
  {
    title: "tmux (TMUX set) → full OpenTUI + reduced-motion auto",
    args: {},
    env: {
      TERM: "xterm-256color",
      COLORTERM: "truecolor",
      TMUX: "/tmp/tmux-1000/default,12345,0",
    },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: false, ascii: false, noMouse: false },
  },
  {
    title: "screen (STY set) → full OpenTUI",
    args: {},
    env: {
      TERM: "xterm-256color",
      COLORTERM: "truecolor",
      STY: "12345.pts-0.host",
    },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: false, ascii: false, noMouse: false },
  },
  {
    title: "tmux + TERM=dumb → ascii fallback",
    args: {},
    env: {
      TERM: "dumb",
      TMUX: "/tmp/tmux-1000/default,12345,0",
    },
    detect: TTY,
    expect: { linear: false, machine: false, noColor: true, ascii: true, noMouse: true },
  },
  {
    title: "SSH + non-TTY → --machine",
    args: {},
    env: {
      TERM: "xterm-256color",
      COLORTERM: "truecolor",
      SSH_CONNECTION: "1.2.3.4 5678 5.6.7.8 22",
    },
    detect: PIPE,
    expect: { linear: false, machine: true, noColor: true, ascii: false, noMouse: true },
  },
]

describe("compatibility matrix — every row of the issue's table", () => {
  for (const row of MATRIX) {
    test(row.title, () => {
      const modes = resolveModes(row.args, row.env, row.detect)
      expect(modes.linear).toBe(row.expect.linear)
      expect(modes.machine).toBe(row.expect.machine)
      expect(modes.noColor).toBe(row.expect.noColor)
      expect(modes.ascii).toBe(row.expect.ascii)
      expect(modes.noMouse).toBe(row.expect.noMouse)
    })
  }
})

describe("compatibility matrix — process.argv / process.env shape", () => {
  test("a synthetic argv + env + detect produces deterministic output", () => {
    const args: Args = { mode: "linear", noColor: true }
    const env = { TERM: "xterm-256color", COLORTERM: "truecolor" }
    const detect = TTY
    const first = resolveModes(args, env, detect)
    const second = resolveModes(args, env, detect)
    expect(first).toEqual(second)
  })

  test("every matrix row keeps the resolver pure — same inputs, same outputs", () => {
    for (const row of MATRIX) {
      const a = resolveModes(row.args, row.env, row.detect)
      const b = resolveModes(row.args, row.env, row.detect)
      expect(a).toEqual(b)
    }
  })
})

describe("compatibility matrix — NO_COLOR wins over capability default", () => {
  test("TTY with COLORTERM but NO_COLOR=1 → no-color on", () => {
    const modes = resolveModes(
      {},
      { TERM: "xterm-256color", COLORTERM: "truecolor", NO_COLOR: "1" },
      TTY
    )
    expect(modes.noColor).toBe(true)
  })

  test("TTY with COLORTERM and NO_COLOR=0 → no-color off (0 means off)", () => {
    const modes = resolveModes(
      {},
      { TERM: "xterm-256color", COLORTERM: "truecolor", NO_COLOR: "0" },
      TTY
    )
    expect(modes.noColor).toBe(false)
  })

  test("--high-contrast + NO_COLOR=1: NO_COLOR wins (terminal off signal)", () => {
    const modes = resolveModes(
      { highContrast: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor", NO_COLOR: "1" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.highContrast).toBe(true)
  })
})

describe("compatibility matrix — stdin TTY + stdout piped", () => {
  test("stdin is a TTY but stdout is piped → machine mode wins", () => {
    const halfPipe: CapabilityDetect = {
      stdoutIsTTY: false,
      stdinIsTTY: true,
      columns: 80,
      rows: 24,
    }
    const modes = resolveModes(
      {},
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      halfPipe
    )
    expect(modes.machine).toBe(true)
  })
})

describe("compatibility matrix — --machine + --mode linear", () => {
  test("--machine wins over --mode linear (user wants NDJSON envelopes)", () => {
    const modes = resolveModes(
      { mode: "linear", machine: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.machine).toBe(true)
    expect(modes.linear).toBe(false)
  })
})

describe("compatibility matrix — capabilities unknown", () => {
  test("no TERM + no COLORTERM + TTY → warns + defaults to safe set", () => {
    const modes = resolveModes({}, {}, TTY)
    expect(modes.warnings.length).toBeGreaterThan(0)
    expect(modes.warnings.some((w) => /no TERM.*COLORTERM/.test(w))).toBe(true)
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.noMouse).toBe(true)
  })

  test("unknown capabilities + explicit --ascii → user override wins, no warning escalates", () => {
    const modes = resolveModes({ ascii: true }, {}, TTY)
    expect(modes.ascii).toBe(true)
    // no-color is still on (the unknown-capability default)
    expect(modes.noColor).toBe(true)
  })
})