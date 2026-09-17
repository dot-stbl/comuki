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
 */
import { Text, useInput } from "ink"
import React, { useCallback, useState } from "react"

export interface PromptInputProps {
  readonly onSubmit: (value: string) => void
  readonly label?: string
  readonly placeholder?: string
  readonly history?: readonly string[]
  /** false → the editor ignores keys (a thinking turn owns the tab). */
  readonly active?: boolean
}

interface EditorState {
  readonly value: string
  readonly cursor: number
  readonly historyIndex: number | null
}

export function PromptInput({
  onSubmit,
  label = "you  ",
  placeholder = "ask comuki… (help for commands, ctrl+c to exit)",
  history = [],
  active = true,
}: PromptInputProps) {
  const [state, setState] = useState<EditorState>({
    value: "",
    cursor: 0,
    historyIndex: null,
  })

  const applyHistory = useCallback(
    (nextIndex: number | null) => {
      if (nextIndex === null || history.length === 0) {
        setState({ value: "", cursor: 0, historyIndex: null })
        return
      }
      const clamped = Math.min(Math.max(0, nextIndex), history.length - 1)
      const value = history[clamped] ?? ""
      setState({ value, cursor: value.length, historyIndex: clamped })
    },
    [history]
  )

  useInput(
    (input, key) => {
      if (key.upArrow) {
        applyHistory(
          state.historyIndex === null
            ? history.length - 1
            : Math.max(0, state.historyIndex - 1)
        )
        return
      }
      if (key.downArrow) {
        if (state.historyIndex !== null) {
          applyHistory(
            state.historyIndex + 1 >= history.length
              ? null
              : state.historyIndex + 1
          )
        }
        return
      }
      if (key.return) {
        setState({ value: "", cursor: 0, historyIndex: null })
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
      <Text color="#8787f3">{label}› </Text>
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
