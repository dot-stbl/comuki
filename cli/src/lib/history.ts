/**
 * Pure helpers behind the prompt's history recall and the ctrl+y
 * copy-last-answer. No React, no Ink — fully testable without a TTY.
 *
 * `historyNavigator` is the single source of the ↑/↓ position rules so
 * `PromptInput` stays a dumb editor and the semantics (draft return,
 * oldest clamping) live in one tested place.
 */
import type { ChatBlock } from "./sessions"

/** ↑ = toward older entries, ↓ = toward newer entries / back to draft. */
export type HistoryDirection = "older" | "newer"

/**
 * Next history index for a recall step.
 *
 * `position` is `null` while the user is on the live draft line; an
 * index into `items` while a recalled entry is being edited.
 *
 * Rules:
 *  - ↑ from the draft jumps to the newest entry (`items.length - 1`).
 *  - ↑ at the oldest entry stays put (position preserved, edit kept).
 *  - ↓ past the newest entry returns to the draft (`null`).
 *  - ↓ on the draft stays on the draft; empty history is always `null`.
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
 * Plain text of the newest assistant message in a transcript, or
 * `undefined` when no assistant message has arrived yet (ctrl+y then
 * hints "nothing to copy" instead of copying user echo).
 */
export function lastAssistantText(
  blocks: readonly ChatBlock[]
): string | undefined {
  for (let index = blocks.length - 1; index >= 0; index--) {
    const block = blocks[index]
    if (block.kind === "message" && block.message.role === "assistant") {
      return block.message.content
    }
  }
  return undefined
}
