/**
 * Terminal control bytes the CLI emits beside its Ink UI: the OSC 2
 * window title, the BEL attention byte, the OSC 9 desktop
 * notification and the OSC 8 hyperlinks that make run ids clickable.
 * Pure builders return the exact bytes (`bun:test` asserts them
 * literally); `writeTerminal` is the single writer and a no-op
 * off-TTY so piped/redirected output never carries control
 * sequences. Terminals that don't know OSC 9 ignore it, and OSC 8
 * degrades to the bare label — the fallback is free, which is the
 * whole reason links ride the same channel.
 */

const OSC = "\x1b]"
const BEL = "\x07"
const ST = "\x1b\\"

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

/**
 * OSC 8 hyperlink: `\x1b]8;;url\x1b\\label\x1b]8;;\x1b\\`. The label
 * is the only visible part, so terminals without OSC 8 render exactly
 * what they rendered before. The url is sanitized (no C0/C1 — a
 * hostile value cannot terminate the sequence early); the label rides
 * through untouched so it may carry SGR paint.
 */
export function linkSequence(url: string, label: string): string {
  return `${OSC}8;;${sanitize(url)}${ST}${label}${OSC}8;;${ST}`
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
