/**
 * Multiline prompt key routing — pure, no Ink types.
 *
 * Terminals disagree wildly on how shift+enter / alt+enter reach the app:
 * ink 5 flags `\r` as `key.return` (modifiers only when the terminal
 * sends a parseable sequence), alt+enter often degrades to a bare `\r`
 * indistinguishable from plain enter, `\n` arrives with no key flags,
 * and kitty-protocol terminals send CSI-u (`\x1b[13;2u` → after ink's
 * esc-strip `"[13;2u"`). The router accepts the raw `input` plus the
 * modifier flags ink reported and decides the one semantic: newline,
 * backslash continuation, or submit.
 */

export type EnterDecision = "newline" | "continue" | "submit"

/** Modifier flags the caller could observe (all optional — terminals differ). */
export interface EnterModifiers {
  readonly return?: boolean
  readonly shift?: boolean
  readonly alt?: boolean
  readonly meta?: boolean
}

/** kitty CSI-u enter with any modifier, after ink strips the leading ESC. */
const kittyEnterRe = /^\[13;\d+u$/

/**
 * Routes one enter-ish keystroke.
 *
 * - modifier + enter (shift/alt/meta flags, or the raw sequences
 *   terminals actually send: LF, kitty CSI-u, and a bare `\r` without
 *   the return flag — that one is `\x1b\r` after ink strips the ESC)
 *   → newline
 * - plain enter on a buffer ending with `\` → continue: the backslash
 *   becomes a newline, cursor to end (shell-style continuation)
 * - plain enter → submit
 *
 * Callers invoke this only for enter-shaped input (`isEnterInput`).
 */
export function routeEnterKey(
  value: string,
  input: string,
  modifiers: EnterModifiers
): EnterDecision {
  if (
    (modifiers.return === true &&
      (modifiers.shift === true ||
        modifiers.alt === true ||
        modifiers.meta === true)) ||
    input === "\n" ||
    kittyEnterRe.test(input) ||
    // Plain `\r` always arrives flagged `return`; an unflagged `\r` is
    // the alt+enter escape sequence with its ESC already stripped.
    (input === "\r" && modifiers.return !== true)
  ) {
    return "newline"
  }
  if (value.endsWith("\\")) {
    return "continue"
  }
  return "submit"
}

/** Is this keystroke enter-shaped at all (worth asking the router)? */
export function isEnterInput(
  input: string,
  modifiers: EnterModifiers
): boolean {
  return (
    modifiers.return === true ||
    input === "\r" ||
    input === "\n" ||
    kittyEnterRe.test(input)
  )
}
