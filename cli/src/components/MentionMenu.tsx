/**
 * The `@mention` autocomplete popup for the chat prompt.
 *
 * All popup logic lives here — `PromptInput` only gains two optional
 * seams (`onDraftChange`, `interceptKey`), so the PromptInput rework
 * lands independently of mentions. The hook debounces a knowledge
 * search while the draft ends in `@qu…` (word start, ≥2 chars), shows
 * up to five `label — snippet` rows above the input (selected row in
 * the brand accent, the rest dim), and takes ↑/↓/Tab/Enter to accept
 * (inserts `@slug `) and Esc to dismiss.
 *
 * `menuOpen`/`rowCount` let the shell shrink the transcript viewport
 * and keep its own tab/esc/arrow hotkeys from firing while the popup
 * owns the keys.
 */
import { Box, Text } from "ink"
import type { Key } from "ink"
import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import {
  activeMentionQuery,
  suggestionRows,
  type MentionSearch,
  type SuggestionRow,
  type TitleLookup,
} from "../lib/mentions"
import { palette } from "../theme"

/** The editor surface `interceptKey` may read and rewrite. */
export interface PromptEditorHandle {
  readonly get: () => { readonly value: string; readonly cursor: number }
  readonly set: (next: { readonly value: string; readonly cursor: number }) => void
}

/**
 * First look at a keystroke: true = consumed (PromptInput must not act
 * on it further). Called before the editor's own handling.
 */
export type InterceptKey = (
  input: string,
  key: Key,
  editor: PromptEditorHandle
) => boolean

/** Props spread onto `PromptInput` — the whole integration surface. */
export interface MentionPromptBindings {
  readonly onDraftChange: (value: string) => void
  readonly interceptKey: InterceptKey
}

export interface MentionMenuOptions {
  readonly search: MentionSearch
  readonly titleFor?: TitleLookup
  /** false → the popup never opens (thinking turn, knowledge off). */
  readonly enabled?: () => boolean
  readonly width?: number
  /** Fired when a search failed — the host flags knowledge off. */
  readonly onUnavailable?: () => void
}

const DEBOUNCE_MS = 250
const MAX_ROWS = 5
const DEFAULT_WIDTH = 72

export function useMentionMenu(options: MentionMenuOptions) {
  const optionsRef = useRef(options)
  useEffect(() => {
    optionsRef.current = options
  })

  const [query, setQuery] = useState<string | null>(null)
  const [rows, setRows] = useState<readonly SuggestionRow[]>([])
  const [selected, setSelected] = useState(0)
  /** The query Esc dismissed — stays closed until the token changes. */
  const dismissedRef = useRef<string | null>(null)

  const onDraftChange = useCallback((value: string) => {
    const current = optionsRef.current
    const enabled = current.enabled === undefined || current.enabled()
    const active = enabled ? activeMentionQuery(value) : null
    if (active === null) {
      setQuery(null)
      return
    }
    // A dismissed token stays dismissed until the token itself changes.
    if (dismissedRef.current !== active) {
      setQuery(active)
    }
  }, [])

  useEffect(() => {
    setSelected(0)
    if (query === null) {
      setRows([])
      return
    }
    let cancelled = false
    const timer = setTimeout(() => {
      void (async () => {
        try {
          const hits = await optionsRef.current.search(query)
          if (!cancelled) {
            setRows(
              suggestionRows(
                hits,
                optionsRef.current.titleFor,
                optionsRef.current.width ?? DEFAULT_WIDTH
              ).slice(0, MAX_ROWS)
            )
          }
        } catch {
          if (!cancelled) {
            setRows([])
            optionsRef.current.onUnavailable?.()
          }
        }
      })()
    }, DEBOUNCE_MS)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [query])

  const interceptKey = useCallback<InterceptKey>(
    (input, key, editor) => {
      if (query === null) {
        return false
      }
      if (key.escape) {
        dismissedRef.current = query
        setQuery(null)
        return true
      }
      if (key.upArrow || key.downArrow) {
        if (rows.length > 0) {
          setSelected((current) =>
            key.upArrow
              ? (current - 1 + rows.length) % rows.length
              : (current + 1) % rows.length
          )
        }
        return true
      }
      if ((key.tab || key.return) && rows.length > 0) {
        const row = rows[selected < rows.length ? selected : 0]
        if (row) {
          // The draft ends in `@query` (that is why the menu is open) —
          // cut the token and drop the slug in its place.
          const value = editor.get().value
          const base = value.slice(0, value.length - query.length - 1)
          const next = `${base}@${row.mention} `
          editor.set({ value: next, cursor: next.length })
        }
        dismissedRef.current = null
        setQuery(null)
        return true
      }
      return false
    },
    [query, rows, selected]
  )

  const menuOpen = query !== null && rows.length > 0
  const element =
    query === null || rows.length === 0 ? null : (
      <Box flexDirection="column">
        {rows.map((row, index) => {
          const isSelected = index === selected
          return (
            <Text key={`${row.mention}-${index}`}>
              {"  "}
              <Text
                color={isSelected ? palette.brand : undefined}
                dimColor={!isSelected}
              >
                {isSelected ? "› " : "  "}
                {row.label}
              </Text>
              <Text dimColor> — {row.snippet}</Text>
            </Text>
          )
        })}
      </Box>
    )

  const promptBindings = useMemo(
    () => ({ onDraftChange, interceptKey }),
    [onDraftChange, interceptKey]
  )

  return {
    /** Popup visible — the shell's hotkey guard keys on this. */
    menuOpen,
    /** Rows the popup currently claims (for the viewport height calc). */
    rowCount: rows.length,
    element,
    promptBindings,
  }
}
