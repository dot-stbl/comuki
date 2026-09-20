/**
 * Composer history recall for the OpenTUI host — the ↑/↓ position
 * rules of the Ink composer's `lib/history.ts`, ported behind the
 * tui/ import boundary (tui never imports ../lib).
 *
 * The navigator is the single source of the recall semantics so the
 * host stays a thin wiring layer:
 *
 *  - ↑ from the live draft jumps to the newest entry and captures the
 *    half-typed draft;
 *  - ↑ at the oldest entry stays put;
 *  - ↓ past the newest entry returns to the captured draft;
 *  - ↓ on the live draft stays live; empty history is always live;
 *  - editing a recalled draft BREAKS recall — the next ↑ starts over
 *    from the newest entry (the edited text becomes the saved draft).
 */

/** ↑ = toward older entries, ↓ = toward newer entries / back to draft. */
export type HistoryDirection = "older" | "newer"

/** The live recall state the host carries between keystrokes. */
export interface HistoryRecallState {
  /**
   * Index into the history while a recalled entry is loaded, `null`
   * while the user is on the live draft line.
   */
  readonly index: number | null
  /** The draft being typed before ↑ first left the live line. */
  readonly savedDraft: string
}

export const LIVE_RECALL: HistoryRecallState = { index: null, savedDraft: "" }

/**
 * Next history index for a recall step (null = the live draft).
 * Pure: position + direction + items → next position.
 */
export function historyNavigator(
  position: number | null,
  direction: HistoryDirection,
  items: readonly string[]
): number | null {
  if (items.length === 0) {
    return null
  }
  // Clamp first: a stale index (history shrank after a restore) must
  // never index out of bounds.
  const clamped =
    position === null ? null : Math.min(Math.max(0, position), items.length - 1)
  if (direction === "older") {
    if (clamped === null) {
      return items.length - 1
    }
    return Math.max(0, clamped - 1)
  }
  if (clamped === null) {
    return null
  }
  const next = clamped + 1
  return next >= items.length ? null : next
}

/**
 * Apply a recall step: the next recall state plus the composer text
 * it should show. Unchanged position (↑ at the oldest, ↓ on the live
 * line) yields `text: null` — the composer keeps what it has.
 */
export function stepRecall(
  state: HistoryRecallState,
  direction: HistoryDirection,
  items: readonly string[]
): { readonly state: HistoryRecallState; readonly text: string | null } {
  const next = historyNavigator(state.index, direction, items)
  if (next === state.index) {
    return { state, text: null }
  }
  if (next === null) {
    return { state: LIVE_RECALL, text: state.savedDraft }
  }
  return {
    state: {
      index: next,
      // The live line was already captured as savedDraft by
      // recallAfterEdit on every edit while live — it rides along.
      savedDraft: state.savedDraft,
    },
    text: items[next] ?? null,
  }
}

/**
 * A user edit landed in the composer. Editing a recalled draft breaks
 * recall: the state returns to the live line with the edited text as
 * the new saved draft, so the next ↑ starts over from the newest
 * entry (the edited text survives a ↓ round-trip).
 */
export function recallAfterEdit(
  state: HistoryRecallState,
  text: string
): HistoryRecallState {
  if (state.index === null) {
    return { index: null, savedDraft: text }
  }
  return { index: null, savedDraft: text }
}

/**
 * Whether ↑/↓ belong to history right now: only from an empty or a
 * recalled draft, and never while a menu/palette surface owns the
 * arrows (the host checks the surface before consulting this).
 */
export function recallArrowsActive(
  state: HistoryRecallState,
  draft: string
): boolean {
  return state.index !== null || draft.length === 0
}
