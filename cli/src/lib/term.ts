/**
 * Terminal control bytes the CLI emits beside its Ink UI: the OSC 2
 * window title, the BEL attention byte and the OSC 9 desktop
 * notification. Pure builders return the exact bytes (`bun:test`
 * asserts them literally); `writeTerminal` is the single writer and a
 * no-op off-TTY so piped/redirected output never carries control
 * sequences. Terminals that don't know OSC 9 ignore it — that is the
 * whole reason BEL and the notification are emitted together.
 */

const OSC = "\x1b]"
const BEL = "\x07"

/** Strips C0/C1 controls so a hostile session title cannot inject sequences. */
function sanitize(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text.replace(/[\x00-\x1f\x7f-\x9f]/g, "")
}

/** OSC 2 — the terminal window title: `\x1b]2;text\x07`. */
export function titleSequence(text: string): string {
  return `${OSC}2;${sanitize(text)}${BEL}`
}

/** The attention byte a terminal translates by its bell/toast rule. */
export function bellSequence(): string {
  return BEL
}

/** OSC 9 desktop notification (iTerm2, WezTerm, Windows Terminal…). */
export function notifySequence(text: string): string {
  return `${OSC}9;${sanitize(text)}${BEL}`
}

/** The app name the title resets to when no session is open / on exit. */
export const TITLE_APP = "comuki"

const GLYPH_THINKING = "⏳"
const GLYPH_IDLE = "✓"

/**
 * `comuki — {session} {glyph}` — ⏳ while the turn is in flight, ✓
 * once it settles. No open session → the bare app name.
 */
export function terminalTitle(
  sessionName: string | undefined,
  thinking: boolean
): string {
  if (!sessionName) {
    return TITLE_APP
  }
  return `${TITLE_APP} — ${sessionName} ${
    thinking ? GLYPH_THINKING : GLYPH_IDLE
  }`
}

/**
 * The bytes for one finished turn: OSC 9 always, BEL only while the
 * `/bell` toggle is on (toasts are cheap, bells are loud).
 */
export function turnDoneSequences(bellEnabled: boolean): string {
  return (
    (bellEnabled ? bellSequence() : "") + notifySequence("comuki: turn done")
  )
}

/** Writes control bytes to the real terminal; no-op when stdout isn't a TTY. */
export function writeTerminal(sequence: string): void {
  if (process.stdout.isTTY) {
    process.stdout.write(sequence)
  }
}
