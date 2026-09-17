/**
 * The `›` prompt with input history and its own line editor.
 *
 * Why not ink-text-input: the multi-session shell needs `tab` (switch
 * session), `esc` (overview), `ctrl+n` / `ctrl+w` (new / close tab) as
 * global hotkeys, and ink-text-input would insert the letter of a
 * ctrl-combo into the buffer — there is no way to consume a key before
 * another `useInput` listener sees it. So editing lives here (insert,
 * backspace, cursor arrows, history); the shell owns the hotkeys in
 * its own `useInput`, and this editor simply ignores those keys.
 *
 * History recall (↑/↓) follows `historyNavigator`: ↑ from the live
 * line saves the half-typed draft and shows the newest entry, ↓ past
 * the newest entry restores that draft, and editing a recalled entry
 * keeps its position so ↑/↓ continue from where the user is.
 */
import { Text, useInput } from "ink"
import React, { useCallback, useEffect, useState } from "react"
import { historyNavigator, type HistoryDirection } from "../lib/history"
import { gutter, palette, symbols } from "../theme"
import type { InterceptKey } from "./MentionMenu"

export interface PromptInputProps {
  readonly onSubmit: (value: string) => void
  readonly label?: string
  readonly placeholder?: string
  readonly history?: readonly string[]
  /** false → the editor ignores keys (a thinking turn owns the tab). */
  readonly active?: boolean
  /**
   * false → ↑/↓ go to the transcript viewport (scrolled-up state)
   * instead of history recall; typing is untouched.
   */
  readonly historyRecallEnabled?: boolean
  /**
   * Mention-menu seam — fires on every draft change so the host can
   * track the active `@token` (debounced search drives the popup).
   */
  readonly onDraftChange?: (value: string) => void
  /**
   * First look at a keystroke: true = consumed. The mention menu eats
   * arrows/tab/enter/escape while its popup is open and rewrites the
   * draft on accept.
   */
  readonly interceptKey?: InterceptKey
}

interface EditorState {
  readonly value: string
  readonly cursor: number
  /**
   * The draft being typed before ↑ first left the live line — restored
   * when ↓ walks back past the newest history entry.
   */
  readonly draft: string
  readonly historyIndex: number | null
}

export function PromptInput({
  onSubmit,
  label = "",
  placeholder = "ask comuki… (help for commands, ctrl+c to exit)",
  history = [],
  active = true,
  historyRecallEnabled = true,
  onDraftChange,
  interceptKey,
}: PromptInputProps) {
  const [state, setState] = useState<EditorState>({
    value: "",
    cursor: 0,
    draft: "",
    historyIndex: null,
  })

  useEffect(() => {
    onDraftChange?.(state.value)
  }, [state.value, onDraftChange])

  const navigate = useCallback(
    (direction: HistoryDirection) => {
      setState((current) => {
        const next = historyNavigator(
          current.historyIndex,
          direction,
          history
        )
        if (next === current.historyIndex) {
          return current
        }
        if (next === null) {
          // Back on the live line — bring the saved draft back.
          return {
            value: current.draft,
            cursor: current.draft.length,
            draft: current.draft,
            historyIndex: null,
          }
        }
        const value = history[next] ?? ""
        return {
          value,
          cursor: value.length,
          // Capture the live line as the draft on the first ↑ only.
          draft: current.historyIndex === null ? current.value : current.draft,
          historyIndex: next,
        }
      })
    },
    [history]
  )

  useInput(
    (input, key) => {
      // The mention menu gets the first look while its popup is open —
      // accept rewrites the draft through the editor handle.
      if (
        interceptKey?.(input, key, {
          get: () => state,
          set: (next) => {
            setState({ ...state, value: next.value, cursor: next.cursor })
          },
        })
      ) {
        return
      }
      if (key.upArrow) {
        // Scrolled-up transcript owns the arrows — the viewport scrolls.
        if (historyRecallEnabled) {
          navigate("older")
        }
        return
      }
      if (key.downArrow) {
        if (historyRecallEnabled) {
          navigate("newer")
        }
        return
      }
      if (key.return) {
        setState({ value: "", cursor: 0, draft: "", historyIndex: null })
        onSubmit(state.value)
        return
      }
      // Everything an editor does not eat is the shell's business:
      // esc, tab, ctrl+n/w/c… — never inserted into the buffer.
      if (
        key.escape ||
        key.tab ||
        key.ctrl ||
        key.meta ||
        key.pageUp ||
        key.pageDown
      ) {
        return
      }
      if (key.leftArrow) {
        setState({ ...state, cursor: Math.max(0, state.cursor - 1) })
        return
      }
      if (key.rightArrow) {
        setState({
          ...state,
          cursor: Math.min(state.value.length, state.cursor + 1),
        })
        return
      }
      if (key.backspace || key.delete) {
        if (state.cursor > 0) {
          setState({
            value:
              state.value.slice(0, state.cursor - 1) +
              state.value.slice(state.cursor),
            cursor: state.cursor - 1,
            draft: state.draft,
            historyIndex: state.historyIndex,
          })
        }
        return
      }
      if (input.length > 0) {
        setState({
          value:
            state.value.slice(0, state.cursor) +
            input +
            state.value.slice(state.cursor),
          cursor: state.cursor + input.length,
          draft: state.draft,
          historyIndex: state.historyIndex,
        })
      }
    },
    { isActive: active }
  )

  const before = state.value.slice(0, state.cursor)
  const at = state.value[state.cursor]
  const after = state.value.slice(state.cursor + 1)

  return (
    <Text>
      {gutter}
      <Text color={palette.brand}>
        {label}
        {symbols.prompt}{" "}
      </Text>
      {state.value.length > 0 ? (
        <>
          <Text>{before}</Text>
          <Text inverse>{at ?? " "}</Text>
          {after.length > 0 ? <Text>{after}</Text> : null}
        </>
      ) : (
        <>
          <Text inverse>{placeholder.slice(0, 1)}</Text>
          <Text dimColor>{placeholder.slice(1)}</Text>
        </>
      )}
    </Text>
  )
}
