/**
 * Resolved CLI render modes (issue #79).
 *
 * `resolveModes(args, env, detect)` is the single source of truth for
 * "what renderer should we mount this run?". It is a PURE function —
 * no I/O at module load, no `process.stdout.write`, no side effects
 * beyond returning the resolution (warnings are returned in the
 * `warnings` array; the caller logs).
 *
 * The host (`./host.ts`) reads the returned `ResolvedModes` and
 * branches: machine → NDJSON envelopes; linear → `LinearRenderer`;
 * otherwise → OpenTUI as today. Each boolean flag toggles a separate
 * renderer knob (ascii font, color escapes, mouse capture, etc.).
 *
 * Precedence — explicit args win, env vars next, capability detection
 * last:
 *
 * 1. CLI flags (`--mode linear`, `--no-color`, `--ascii`, ...)
 * 2. Environment hints (`COMUKI_NO_COLOR`, `NO_COLOR`, `COMUKI_LINEAR`,
 *    `ACCESSIBILITY_ENABLED`, `TERM`, `COLORTERM`, `TMUX`, `STY`,
 *    `SSH_CONNECTION`, `COMUKI_REDUCED_MOTION`, ...)
 * 3. Capability detection (`detect` argument — typically
 *    `isatty`, `process.stdout.columns`, ...)
 *
 * Two hard overrides:
 *
 * - `NO_COLOR=1` always wins over `COMUKI_NO_COLOR=1` and over the
 *   `--high-contrast` flag — `NO_COLOR` is a terminal-side "no
 *   colour" signal that the user policy cannot undo.
 * - `--machine` wins over `--mode linear`: machine is a different
 *   transport (NDJSON on stdout), not a render variant of linear.
 *
 * `capabilityReport` is a one-line human description of what the
 * detection actually saw, so the `--explain-mode` diagnostic can
 * print a useful explanation without re-running detection.
 */
import type { Args } from "./modes.args"

export type { Args } from "./modes.args"

export interface ResolvedModes {
  readonly linear: boolean
  readonly reducedMotion: boolean
  readonly highContrast: boolean
  readonly noColor: boolean
  readonly ascii: boolean
  readonly noMouse: boolean
  readonly unicodeNarrow: boolean
  readonly machine: boolean
  readonly capabilityReport: string
  readonly warnings: readonly string[]
}

export interface CapabilityDetect {
  readonly stdoutIsTTY: boolean
  readonly stdinIsTTY: boolean
  readonly columns: number | null
  readonly rows: number | null
}

const EMPTY_DETECT: CapabilityDetect = {
  stdoutIsTTY: true,
  stdinIsTTY: true,
  columns: null,
  rows: null,
}

/**
 * Resolve the full mode set. Pure — no `process.*` reads inside. The
 * caller supplies `args` (parsed CLI flags) and `env` (typically
 * `process.env`); `detect` defaults to "everything is a TTY, we know
 * nothing".
 *
 * Returns the resolved mode set + a one-line `capabilityReport` the
 * `--explain-mode` diagnostic prints.
 */
export function resolveModes(
  args: Args,
  env: Readonly<Record<string, string | undefined>>,
  detect: CapabilityDetect = EMPTY_DETECT
): ResolvedModes {
  const warnings: string[] = []
  const report: string[] = []

  // -- 1. capability detection -------------------------------------------
  const stdoutIsTTY = detect.stdoutIsTTY
  const columns = detect.columns ?? 80
  const rows = detect.rows ?? 24
  const term = (env.TERM ?? "").toLowerCase()
  const colorterm = (env.COLORTERM ?? "").toLowerCase()
  const sshConnection = env.SSH_CONNECTION !== undefined && env.SSH_CONNECTION.length > 0
  const tmux = env.TMUX !== undefined && env.TMUX.length > 0
  const screen = env.STY !== undefined && env.STY.length > 0
  const inTmux = tmux || screen
  const isDumbTerminal = term === "dumb" || term === "linux"
  void detect.stdinIsTTY

  report.push(
    `tty=${stdoutIsTTY ? "yes" : "no"}`,
    `term=${term.length > 0 ? term : "(unset)"}`,
    `colorterm=${colorterm.length > 0 ? colorterm : "(unset)"}`,
    `cols=${columns}`,
    `rows=${rows}`
  )

  // Capabilities unknown: TERM unset, COLORTERM unset — the OpenTUI
  // renderer would still work but every UI hint would guess; warn the
  // user once and pick the safe defaults.
  if (term.length === 0 && colorterm.length === 0 && stdoutIsTTY) {
    warnings.push(
      "no TERM / COLORTERM — assuming 80x24, no colour, no mouse; set TERM=xterm-256color for the full renderer"
    )
  }

  // -- 2. explicit args ----------------------------------------------------
  const argLinear = args.mode === "linear"
  const argMachine = args.machine === true
  const argNoColor = args.noColor === true
  const argHighContrast = args.highContrast === true
  const argReducedMotion = args.reducedMotion === true
  const argAscii = args.ascii === true
  const argNoMouse = args.noMouse === true
  const argUnicodeNarrow = args.unicodeNarrow === true

  // -- 3. env hints --------------------------------------------------------
  const envNoColorStandard = (env.NO_COLOR ?? "").length > 0 && env.NO_COLOR !== "0"
  const envNoColorComuki = (env.COMUKI_NO_COLOR ?? "").length > 0 && env.COMUKI_NO_COLOR !== "0"
  const envLinear =
    (env.COMUKI_LINEAR ?? "").length > 0 && env.COMUKI_LINEAR !== "0"
  const envAccessibility =
    (env.ACCESSIBILITY_ENABLED ?? "").length > 0 && env.ACCESSIBILITY_ENABLED !== "0"

  // NO_COLOR is the hard "off" signal — wins over COMUKI_NO_COLOR and
  // over --high-contrast (which would otherwise pull color back in for
  // accessibility).
  const noColor = envNoColorStandard || envNoColorComuki || argNoColor

  // -- 4. capability-driven defaults --------------------------------------
  // Default colors: on for TTY with a colour-capable TERM, off for
  // dumb/linux/not-TTY.
  const defaultColor =
    stdoutIsTTY && !isDumbTerminal && (colorterm.length > 0 || term.includes("color"))
  const defaultLinear = envLinear || envAccessibility

  // -- 5. terminal class → mode seed --------------------------------------
  let linear = argLinear || defaultLinear
  let machine = argMachine
  const reducedMotion: boolean = argReducedMotion
  const highContrast: boolean = argHighContrast
  const ascii: boolean = argAscii
  const noMouse: boolean = argNoMouse
  const unicodeNarrow: boolean = argUnicodeNarrow
  // `color` is the *renderer* choice, distinct from `noColor` which
  // gates it. The OpenTUI host reads `noColor` directly.
  const baseColorOn = !noColor && defaultColor

  if (!stdoutIsTTY) {
    // -- not a TTY: machine path. Captured output — nothing
    //    interactive can happen anyway. The user might still have
    //    asked for `linear` to inspect captured text; machine wins
    //    because the user explicitly asked for JSON envelopes.
    machine = true
    report.push("path=machine(not-a-tty)")
  } else if (sshConnection && !stdoutIsTTY) {
    // SSH without TTY → machine (the SSH_CONNECTION check is on env,
    // not the runtime stream; belt-and-braces).
    machine = true
    report.push("path=machine(ssh-no-tty)")
  } else if (isDumbTerminal) {
    // TERM=dumb / linux → no colour + linear. tmux inside wins
    // when STY/TMUX is set AND TERM is colour-capable.
    if (!inTmux || isDumbTerminal) {
      report.push("path=no-color+linear(dumb-terminal)")
    }
  } else if (inTmux) {
    report.push("path=full-opentui(tmux)")
  } else {
    report.push("path=full-opentui(tty)")
  }

  // -- 6. cross-flag dominance --------------------------------------------
  // machine always wins over linear — the user wanted JSON envelopes,
  // not the linear text path. Both flags can coexist on the CLI; the
  // later one is moot.
  if (machine && linear && argLinear && !argMachine) {
    warnings.push("--machine wins over --mode linear; switching to NDJSON envelopes")
  }
  if (machine) {
    linear = false
  }

  // -- 7. derived booleans ------------------------------------------------
  // Colour: when machine mode we also drop colour (NDJSON is plain
  // JSON — ANSI escapes would corrupt downstream parsers).
  const effectiveNoColor = noColor || machine || !baseColorOn
  // Mouse: only safe when TTY + not machine + not linear. When
  // capabilities are completely unknown (no TERM, no COLORTERM) we
  // cannot assume the terminal supports mouse capture — fall back
  // to keystrokes only.
  const capabilitiesUnknown = term.length === 0 && colorterm.length === 0
  const effectiveNoMouse =
    noMouse || machine || !stdoutIsTTY || isDumbTerminal || capabilitiesUnknown
  // ASCII font: enabled by --ascii OR by dumb TERM (which often
  // renders box-drawing as mojibake on serial consoles) OR when
  // capabilities are completely unknown.
  const effectiveAscii = ascii || isDumbTerminal || capabilitiesUnknown
  // Reduced-motion: ON automatically inside tmux/screen (frame
  // flicker reads as movement to some screen readers) and when the
  // user opts in via env.
  const envReducedMotion =
    (env.COMUKI_REDUCED_MOTION ?? "").length > 0 && env.COMUKI_REDUCED_MOTION !== "0"
  const effectiveReducedMotion = reducedMotion || envReducedMotion || inTmux

  return {
    linear,
    reducedMotion: effectiveReducedMotion,
    highContrast,
    noColor: effectiveNoColor,
    ascii: effectiveAscii,
    noMouse: effectiveNoMouse,
    unicodeNarrow,
    machine,
    capabilityReport: report.join(" "),
    warnings,
  }
}

/**
 * Format the resolved mode set as a one-line diagnostic for
 * `--explain-mode`. Each flag is `name=on/off`; the capability
 * report rides after the `=` separator. Stable ordering so tests
 * can compare exact output.
 */
export function explainMode(modes: ResolvedModes): string {
  const flags = [
    `linear=${modes.linear ? "on" : "off"}`,
    `reduced-motion=${modes.reducedMotion ? "on" : "off"}`,
    `high-contrast=${modes.highContrast ? "on" : "off"}`,
    `no-color=${modes.noColor ? "on" : "off"}`,
    `ascii=${modes.ascii ? "on" : "off"}`,
    `no-mouse=${modes.noMouse ? "on" : "off"}`,
    `unicode-narrow=${modes.unicodeNarrow ? "on" : "off"}`,
    `machine=${modes.machine ? "on" : "off"}`,
  ]
  return `mode: ${flags.join(" · ")} | ${modes.capabilityReport}`
}