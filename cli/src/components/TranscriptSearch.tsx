/**
 * The ctrl+f transcript search row — a single-line inline editor that
 * pins above the footer while open. Same philosophy as PromptInput: a
 * hand-rolled mini editor instead of ink-text-input, so the shell's
 * hotkeys (tab, ctrl+n/w, esc…) never leak letters into the query
 * buffer — the shell gates its own useInput while `searchOpen` and the
 * prompt goes inactive; this row owns the keyboard for the duration.
 *
 * Pure matching lives in `lib/transcript.ts` (`findMatches`,
 * `nextMatchIndex`, `highlightLine`); this component only edits the
 * query and reports enter (next), shift+enter (previous) and esc
 * (close). The `matchIndex`/`matchCount` pair renders the `3/7`
 * counter; `matchIndex` is 0-based and already clamped by the host.
 */
import { Box, Text, useInput } from "ink"
import React, { useState } from "react"
import { isEnterInput, routeEnterKey } from "../lib/multiline"
import { gutter, palette, symbols } from "../theme"

export interface TranscriptSearchProps {
  /** The live query — owned by the shell, reported back through onChange. */
  readonly value: string
  /** How many flattened lines match. */
  readonly matchCount: number
  /** 0-based cursor into the match list (clamped by the host). */
  readonly matchIndex: number
  readonly onChange: (value: string) => void
  /** Plain enter — jump to the next match. */
  readonly onNext: () => void
  /** Shift+enter — jump to the previous match. */
  readonly onPrevious: () => void
  /** Esc — close the search and clear the query. */
  readonly onClose: () => void
}

export function TranscriptSearch({
  value,
  matchCount,
  matchIndex,
  onChange,
  onNext,
  onPrevious,
  onClose,
}: TranscriptSearchProps) {
  // The caret starts at the end of the initial query (the shell always
  // opens the search empty, so in practice this is 0).
  const [cursor, setCursor] = useState(value.length)

  useInput((input, key) => {
    if (key.escape) {
      onClose()
      return
    }
    // Terminals disagree on modified enter (kitty CSI-u arrives
    // ESC-stripped as `"[13;2u"`, alt+enter degrades to a bare `\r`) —
    // the prompt's router already normalizes those shapes. Here its
    // "newline" (a modified enter) reads as previous-match.
    if (isEnterInput(input, key)) {
      if (routeEnterKey("", input, key) === "newline") {
        onPrevious()
      } else {
        onNext()
      }
      return
    }
    // Not this editor's business: shell combos, scroll keys, history
    // arrows — consumed so they never reach another listener's intent.
    if (
      key.tab ||
      key.ctrl ||
      key.meta ||
      key.pageUp ||
      key.pageDown ||
      key.upArrow ||
      key.downArrow
    ) {
      return
    }
    if (key.leftArrow) {
      setCursor((current) => Math.max(0, current - 1))
      return
    }
    if (key.rightArrow) {
      setCursor((current) => Math.min(value.length, current + 1))
      return
    }
    if (key.backspace || key.delete) {
      if (cursor > 0) {
        onChange(value.slice(0, cursor - 1) + value.slice(cursor))
        setCursor(cursor - 1)
      }
      return
    }
    if (input.length > 0) {
      onChange(value.slice(0, cursor) + input + value.slice(cursor))
      setCursor((current) => current + input.length)
    }
  })

  const at = value[cursor]
  const before = value.slice(0, cursor)
  const after = at === undefined ? "" : value.slice(cursor + 1)
  const counter =
    matchCount > 0 ? `${matchIndex + 1}/${matchCount}` : `0/${matchCount}`

  return (
    <Box>
      <Text>
        {gutter}
        <Text color={palette.brand}>{`${symbols.prompt} find `}</Text>
        <Text>{before}</Text>
        <Text inverse>{at ?? " "}</Text>
        {after.length > 0 ? <Text>{after}</Text> : null}
        <Text dimColor>{`  ${counter} · enter next · esc close`}</Text>
      </Text>
    </Box>
  )
}
