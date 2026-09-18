/**
 * Pure action list for the expandable session footer.
 *
 * Always-on items first, then the three state-gated ones (login when
 * signed out, approve/reject while a plan waits, stop while thinking),
 * then help / quit. Tests drive this without Ink.
 */
export interface FooterAction {
  readonly id: string
  readonly label: string
  readonly hint?: string
}

export interface FooterActionState {
  readonly thinking: boolean
  readonly awaitingApproval: boolean
  readonly signedOut: boolean
  readonly sessionCount: number
}

export const FOOTER_EXPAND_HINT = "ctrl+/ actions"

const ALWAYS_ON: readonly FooterAction[] = [
  { id: "new", label: "new session", hint: "ctrl+n" },
  { id: "overview", label: "overview", hint: "esc" },
  { id: "verbose", label: "verbose", hint: "ctrl+o" },
  { id: "copy", label: "copy last", hint: "ctrl+y" },
  { id: "search", label: "search", hint: "ctrl+f" },
]

const TAIL: readonly FooterAction[] = [
  { id: "help", label: "help" },
  { id: "quit", label: "quit" },
]

/**
 * Derives the visible action list from session + identity flags.
 * `sessionCount` is reserved for callers that want to hide "new" when
 * the tab strip is empty — currently unused (new is always available).
 */
export function footerActions(state: FooterActionState): FooterAction[] {
  const gated: FooterAction[] = []
  if (state.signedOut) {
    gated.push({ id: "login", label: "login" })
  }
  if (state.awaitingApproval) {
    gated.push(
      { id: "approve", label: "approve" },
      { id: "reject", label: "reject" }
    )
  }
  if (state.thinking) {
    gated.push({ id: "stop", label: "stop" })
  }
  return [...ALWAYS_ON, ...gated, ...TAIL]
}

/** Wrap-around index for ↑/↓ / ←/→; empty list stays at 0. */
export function wrapActionIndex(
  index: number,
  count: number,
  delta: 1 | -1
): number {
  if (count <= 0) {
    return 0
  }
  return (index + delta + count) % count
}

/**
 * Collapsed footer is one row (badges + hint). Expanded adds a dim
 * top rule plus one row per action on top of that collapsed row.
 * Callers subtract this from the transcript viewport the same way
 * they subtract PromptInput rows.
 */
export function footerRowCount(
  expanded: boolean,
  actionCount: number
): number {
  if (!expanded) {
    return 1
  }
  return 1 + 1 + Math.max(0, actionCount)
}

/**
 * Maps a 1-based SGR click `y` onto an action index while the bar is
 * expanded. The dim rule is `footerTopY`; actions start on the next
 * row. Returns null when the click missed the list.
 */
export function hitTestFooterAction(
  y: number,
  footerTopY: number,
  actionCount: number
): number | null {
  if (actionCount <= 0) {
    return null
  }
  const index = y - footerTopY - 1
  if (index < 0 || index >= actionCount) {
    return null
  }
  return index
}

/**
 * 1-based SGR row of the first footer line (the dim rule when expanded,
 * the badge row when collapsed). Status line is always row 1.
 */
export function footerTopRow(input: {
  readonly hasTabBar: boolean
  readonly viewportHeight: number
  readonly searchOpen: boolean
}): number {
  return (
    1 +
    (input.hasTabBar ? 1 : 0) +
    input.viewportHeight +
    (input.searchOpen ? 1 : 0) +
    1
  )
}

/** True when a click on the collapsed footer row should expand. */
export function isCollapsedFooterClick(
  y: number,
  footerTopY: number,
  expanded: boolean
): boolean {
  return !expanded && y === footerTopY
}

/** Badge/hint row while expanded — clicking it collapses. */
export function footerHintRow(
  footerTopY: number,
  actionCount: number
): number {
  return footerTopY + 1 + Math.max(0, actionCount)
}

export interface ExpandChordKey {
  readonly ctrl: boolean
}

/**
 * `ctrl+/` — the expand/collapse chord. Terminals disagree on the
 * byte (`/`, `?`, or the C0 `US` 0x1F that some send for ctrl+/), so
 * all three count when the ctrl flag is on.
 */
export function isFooterExpandChord(
  input: string,
  key: ExpandChordKey
): boolean {
  if (!key.ctrl) {
    return false
  }
  return input === "/" || input === "?" || input === "\x1f"
}
