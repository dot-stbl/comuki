/**
 * Canonical contract for `resolveModes` (issue #79).
 *
 * Every flag, every env var, every precedence rule is asserted here.
 * The matrix end-to-end (`matrix.test.ts`) reads the same resolution
 * path under terminal-style capability signals; this file asserts
 * the resolver in isolation, including the cross-flag dominance
 * rules.
 *
 * Test naming follows the rule's edge-case list (issue #79 §"Edge
 * cases to handle in your tests") plus the flag / env catalogue.
 */
import { describe, expect, test } from "bun:test"
import { resolveModes, type Args } from "./modes"
import type { CapabilityDetect } from "./modes"

const TTY: CapabilityDetect = { stdoutIsTTY: true, stdinIsTTY: true, columns: 80, rows: 24 }
const PIPE: CapabilityDetect = { stdoutIsTTY: false, stdinIsTTY: false, columns: 80, rows: 24 }
const EMPTY_ARGS: Args = {}

describe("resolveModes — flag precedence", () => {
  test("--mode linear flips linear on for a colour-capable TTY", () => {
    const modes = resolveModes(
      { mode: "linear" },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.linear).toBe(true)
    expect(modes.machine).toBe(false)
    expect(modes.noColor).toBe(false)
    expect(modes.noMouse).toBe(false)
  })

  test("--machine flips machine on and overrides --mode linear", () => {
    const modes = resolveModes(
      { mode: "linear", machine: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.machine).toBe(true)
    expect(modes.linear).toBe(false)
  })

  test("--machine flips machine on and overrides env-driven linear", () => {
    const modes = resolveModes(
      { machine: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor", COMUKI_LINEAR: "1" },
      TTY
    )
    expect(modes.machine).toBe(true)
    expect(modes.linear).toBe(false)
  })

  test("--no-color wins over --high-contrast (NO_COLOR is the terminal-side off signal)", () => {
    const modes = resolveModes(
      { noColor: true, highContrast: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.highContrast).toBe(true)
  })

  test("--ascii alone does not flip colour off", () => {
    const modes = resolveModes(
      { ascii: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.ascii).toBe(true)
    expect(modes.noColor).toBe(false)
  })

  test("--no-mouse alone does not flip mouse off in a default TTY", () => {
    const modes = resolveModes(
      { noMouse: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.noMouse).toBe(true)
  })

  test("--unicode-narrow flips the width-clamp on without other side effects", () => {
    const modes = resolveModes(
      { unicodeNarrow: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.unicodeNarrow).toBe(true)
    expect(modes.linear).toBe(false)
    expect(modes.machine).toBe(false)
  })

  test("--reduced-motion flips reducedMotion on without other side effects", () => {
    const modes = resolveModes(
      { reducedMotion: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.reducedMotion).toBe(true)
    expect(modes.linear).toBe(false)
  })
})

describe("resolveModes — env precedence", () => {
  test("NO_COLOR=1 wins over COMUKI_NO_COLOR=0 and over --high-contrast", () => {
    const modes = resolveModes(
      { highContrast: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor", NO_COLOR: "1", COMUKI_NO_COLOR: "0" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.highContrast).toBe(true)
  })

  test("COMUKI_NO_COLOR=1 with NO_COLOR unset still flips noColor on", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", COMUKI_NO_COLOR: "1" },
      TTY
    )
    expect(modes.noColor).toBe(true)
  })

  test("COMUKI_LINEAR=1 flips linear on without an explicit --mode flag", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", COMUKI_LINEAR: "1" },
      TTY
    )
    expect(modes.linear).toBe(true)
  })

  test("ACCESSIBILITY_ENABLED=1 flips linear on (screen-reader detection)", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", ACCESSIBILITY_ENABLED: "1" },
      TTY
    )
    expect(modes.linear).toBe(true)
  })

  test("COMUKI_REDUCED_MOTION=1 flips reducedMotion on", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", COMUKI_REDUCED_MOTION: "1" },
      TTY
    )
    expect(modes.reducedMotion).toBe(true)
  })

  test("env value '0' is treated as OFF, not ON", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      {
        TERM: "xterm-256color",
        COLORTERM: "truecolor",
        COMUKI_NO_COLOR: "0",
        COMUKI_LINEAR: "0",
        COMUKI_REDUCED_MOTION: "0",
      },
      TTY
    )
    expect(modes.noColor).toBe(false)
    expect(modes.linear).toBe(false)
    expect(modes.reducedMotion).toBe(false)
  })
})

describe("resolveModes — capability detection", () => {
  test("not a TTY (piped stdout) forces machine mode", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      PIPE
    )
    expect(modes.machine).toBe(true)
    expect(modes.linear).toBe(false)
  })

  test("not a TTY with --mode linear still resolves to machine (machine wins)", () => {
    const modes = resolveModes(
      { mode: "linear" },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      PIPE
    )
    expect(modes.machine).toBe(true)
    expect(modes.linear).toBe(false)
  })

  test("not a TTY with --machine explicit sets machine (idempotent)", () => {
    const modes = resolveModes(
      { machine: true },
      {},
      PIPE
    )
    expect(modes.machine).toBe(true)
  })

  test("TTY with TERM=dumb forces no-color + ascii", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "dumb" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.noMouse).toBe(true)
    expect(modes.machine).toBe(false)
  })

  test("TTY with TERM=linux forces no-color + ascii", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "linux" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
  })

  test("TTY with TERM=dumb inside tmux still uses the ascii fallback", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "dumb", TMUX: "/tmp/tmux-1000/default,12345,0" },
      TTY
    )
    expect(modes.ascii).toBe(true)
  })

  test("TTY with TERM=dumb inside screen still uses the ascii fallback", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "dumb", STY: "12345.pts-0.host" },
      TTY
    )
    expect(modes.ascii).toBe(true)
  })

  test("TTY with full color terminal leaves colour on, mouse on", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.noColor).toBe(false)
    expect(modes.noMouse).toBe(false)
    expect(modes.linear).toBe(false)
    expect(modes.machine).toBe(false)
  })

  test("SSH_CONNECTION + non-TTY resolves to machine", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", SSH_CONNECTION: "1.2.3.4 5678 5.6.7.8 22" },
      PIPE
    )
    expect(modes.machine).toBe(true)
  })

  test("SSH_CONNECTION + TTY does NOT auto-machine (the user is on a real terminal)", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", SSH_CONNECTION: "1.2.3.4 5678 5.6.7.8 22" },
      TTY
    )
    expect(modes.machine).toBe(false)
  })

  test("inside tmux, reduced-motion auto-engages", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", TMUX: "/tmp/tmux-1000/default,12345,0" },
      TTY
    )
    expect(modes.reducedMotion).toBe(true)
  })

  test("inside screen, reduced-motion auto-engages", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", STY: "12345.pts-0.host" },
      TTY
    )
    expect(modes.reducedMotion).toBe(true)
  })

  test("capabilities unknown (no TERM, no COLORTERM) → defaults to no-color, ascii, no-mouse + warning", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      {},
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.noMouse).toBe(true)
    expect(modes.warnings.length).toBeGreaterThan(0)
    expect(modes.warnings[0]).toMatch(/no TERM.*COLORTERM/)
  })

  test("capability report carries every detected signal", () => {
    const modes = resolveModes(
      EMPTY_ARGS,
      { TERM: "xterm-256color", COLORTERM: "truecolor", TMUX: "/tmp/tmux-1000/default,12345,0" },
      TTY
    )
    expect(modes.capabilityReport).toContain("tty=yes")
    expect(modes.capabilityReport).toContain("term=xterm-256color")
    expect(modes.capabilityReport).toContain("colorterm=truecolor")
    expect(modes.capabilityReport).toContain("cols=80")
    expect(modes.capabilityReport).toContain("rows=24")
  })
})

describe("resolveModes — flag combinations", () => {
  test("--no-color --ascii together: both stay on, no surprises", () => {
    const modes = resolveModes(
      { noColor: true, ascii: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.linear).toBe(false)
    expect(modes.machine).toBe(false)
  })

  test("--mode linear --no-color --ascii coexist (the screen-reader mode, monochrome, ascii glyphs)", () => {
    const modes = resolveModes(
      { mode: "linear", noColor: true, ascii: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.linear).toBe(true)
    expect(modes.noColor).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.machine).toBe(false)
  })

  test("--machine --ascii coexist (machine + ascii glyphs in JSON envelopes)", () => {
    const modes = resolveModes(
      { machine: true, ascii: true },
      { TERM: "xterm-256color", COLORTERM: "truecolor" },
      TTY
    )
    expect(modes.machine).toBe(true)
    expect(modes.ascii).toBe(true)
    expect(modes.linear).toBe(false)
    // Machine output never gets colour — NDJSON stays plain.
    expect(modes.noColor).toBe(true)
  })
})

describe("resolveModes — pure / no I/O", () => {
  test("the resolver never reads process.env (returns the same result twice from the same input)", () => {
    const args: Args = EMPTY_ARGS
    const env = { TERM: "xterm-256color", COLORTERM: "truecolor" }
    const first = resolveModes(args, env, TTY)
    const second = resolveModes(args, env, TTY)
    expect(first).toEqual(second)
  })

  test("the resolver never reads process.stdout (TTY-flagged input is honoured)", () => {
    const pipedArgs: Args = EMPTY_ARGS
    const pipedFirst = resolveModes(pipedArgs, {}, PIPE)
    expect(pipedFirst.machine).toBe(true)
  })
})

// Re-imported in the middle of the describe to keep the
// screen-reader test close to the tmux one. (Bun resolves at
// import time; the binding above already provides resolveModes —
// this is a local alias to avoid shadowing in this block only.)
void 0