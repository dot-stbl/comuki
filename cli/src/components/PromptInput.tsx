/**
 * The `›` prompt with input history, slash autocomplete and a
 * multiline line editor.
 *
 * Why not ink-text-input: the multi-session shell needs `tab` (switch
 * session), `esc` (overview), `ctrl+n` / `ctrl+w` (new / close tab) as
 * global hotkeys, and ink-text-input would insert the letter of a
 * ctrl-combo into the buffer — there is no way to consume a key before
 * another `useInput` listener sees it. So editing lives here (insert,
 * backspace, cursor arrows, history); the shell owns the hotkeys in
 * its own `useInput`, and this editor simply ignores those keys.
 *
 * Cooperative key ownership while the slash menu is open: the menu
 * takes ↑/↓ (selection), tab/enter (complete) and esc (dismiss); the
 * shell learns the menu is open through `onMenuOpenChange` and holds
 * back its own tab/esc/arrow handling for those keystrokes.
 *
 * Multiline: modifier+enter (shift/alt — as flags or the raw sequences
 * terminals actually send) and a trailing `\` + plain enter insert a
 * newline; plain enter submits. The routing itself is
 * `lib/multiline.ts` (pure), the value can span lines, and the prompt
 * block reports its rendered height via `onRowsChange` so the shell's
 * viewport math stays honest.
 *
 * History recall (↑/↓) follows `historyNavigator`: ↑ from the live
 * line saves the half-typed draft and shows the newest entry, ↓ past
 * the newest entry restores that draft, and editing a recalled entry
 * keeps its position so ↑/↓ continue from where the user is.
 */
import { Box, Text, useInput } from "ink"
import React, { useCallback, useEffect, useRef, useState } from "react"
import { historyNavigator, type HistoryDirection } from "../lib/history"
import { isEnterInput, routeEnterKey } from "../lib/multiline"
import {
  SLASH_COMMANDS,
  completeSlashCommand,
  filterSlashCommands,
  slashMenuQuery,
  type SlashCommand,
} from "../lib/slash"
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
   * false → ↑/↓ (and vim j/k/g/G) go to the transcript viewport
   * (scrolled-up state) instead of history recall / insert.
   */
  readonly historyRecallEnabled?: boolean
  /** Fired on every open/close flip — the shell gates its hotkeys on it. */
  readonly onMenuOpenChange?: (open: boolean) => void
  /**
   * Fired when the rendered row count changes (menu rows + wrapped
   * input lines) — the shell subtracts it from the viewport height.
   */
  readonly onRowsChange?: (rows: number) => void
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
  /**
   * Prefill seam — when `seq` changes the editor value is replaced
   * (cursor at the end). Same seq is a no-op so typing is not clobbered
   * by a parent re-render. `/edit` uses this; it never submits.
   */
  readonly seed?: { readonly value: string; readonly seq: number }
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
  /** Selected row in the slash menu; wrapped by ↑/↓ while it is open. */
  readonly menuIndex: number
  /** Esc closed the menu; any further typing reopens it (new query). */
  readonly menuDismissed: boolean
}

const FRESH_EDITOR: EditorState = {
  value: "",
  cursor: 0,
  draft: "",
  historyIndex: null,
  menuIndex: 0,
  menuDismissed: false,
}

export function PromptInput({
  onSubmit,
  label = "",
  placeholder = "ask comuki… (help for commands, ctrl+c to exit)",
  history = [],
  active = true,
  historyRecallEnabled = true,
  onMenuOpenChange,
  onRowsChange,
  onDraftChange,
  interceptKey,
  seed,
}: PromptInputProps) {
  const [state, setState] = useState<EditorState>(FRESH_EDITOR)
  const seedSeqRef = useRef<number | undefined>(undefined)

  const query = slashMenuQuery(state.value)
  const matches =
    query === null
      ? []
      : filterSlashCommands(SLASH_COMMANDS, query)
  const menuOpen = query !== null && !state.menuDismissed && matches.length > 0

  /**
   * Any value mutation goes through here: the menu selection resets
   * (the filter just changed underneath it) and a dismissal is lifted
   * so typing after Esc reopens the menu for the new query.
   */
  const edit = useCallback(
    (updater: (current: EditorState) => EditorState) => {
      setState((current) => {
        const next = updater(current)
        if (next.value === current.value) {
          return next
        }
        return { ...next, menuIndex: 0, menuDismissed: false }
      })
    },
    []
  )

  useEffect(() => {
    onDraftChange?.(state.value)
  }, [state.value, onDraftChange])

  useEffect(() => {
    if (seed === undefined || seed.seq === seedSeqRef.current) {
      return
    }
    seedSeqRef.current = seed.seq
    setState({
      value: seed.value,
      cursor: seed.value.length,
      draft: "",
      historyIndex: null,
      menuIndex: 0,
      menuDismissed: false,
    })
  }, [seed])

  const navigate = useCallback(
    (direction: HistoryDirection) => {
      edit((current) => {
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
            menuIndex: current.menuIndex,
            menuDismissed: current.menuDismissed,
          }
        }
        const value = history[next] ?? ""
        return {
          value,
          cursor: value.length,
          // Capture the live line as the draft on the first ↑ only.
          draft: current.historyIndex === null ? current.value : current.draft,
          historyIndex: next,
          menuIndex: current.menuIndex,
          menuDismissed: current.menuDismissed,
        }
      })
    },
    [edit, history]
  )

  const completeFromMenu = useCallback(
    (command: SlashCommand) => {
      const completed = completeSlashCommand(command)
      setState({
        value: completed,
        cursor: completed.length,
        draft: "",
        historyIndex: null,
        menuIndex: 0,
        // The trailing space closes the menu by itself (whitespace in
        // the query) — no dismissal needed.
        menuDismissed: false,
      })
    },
    []
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
      if (menuOpen) {
        if (key.upArrow) {
          const nextIndex =
            (state.menuIndex - 1 + matches.length) % matches.length
          setState({ ...state, menuIndex: nextIndex })
          return
        }
        if (key.downArrow) {
          const nextIndex = (state.menuIndex + 1) % matches.length
          setState({ ...state, menuIndex: nextIndex })
          return
        }
        // Plain tab/enter complete; a modified enter still newlines.
        if (
          (key.tab || key.return) &&
          !key.shift &&
          !key.meta
        ) {
          const chosen = matches[state.menuIndex] ?? matches[0]
          if (chosen) {
            completeFromMenu(chosen)
          }
          return
        }
        if (key.escape) {
          setState({ ...state, menuDismissed: true })
          return
        }
      }
      if (
        key.upArrow &&
        !menuOpen &&
        historyRecallEnabled
      ) {
        navigate("older")
        return
      }
      if (
        key.downArrow &&
        !menuOpen &&
        historyRecallEnabled
      ) {
        navigate("newer")
        return
      }
      if (isEnterInput(input, key)) {
        const decision = routeEnterKey(state.value, input, key)
        if (decision === "newline") {
          edit((current) => ({
            value:
              current.value.slice(0, current.cursor) +
              "\n" +
              current.value.slice(current.cursor),
            cursor: current.cursor + 1,
            draft: current.draft,
            historyIndex: current.historyIndex,
            menuIndex: current.menuIndex,
            menuDismissed: current.menuDismissed,
          }))
          return
        }
        if (decision === "continue") {
          // Backslash continuation: the trailing `\` becomes the newline.
          edit((current) => {
            const trimmed = current.value.slice(0, -1) + "\n"
            return {
              value: trimmed,
              cursor: trimmed.length,
              draft: current.draft,
              historyIndex: current.historyIndex,
              menuIndex: current.menuIndex,
              menuDismissed: current.menuDismissed,
            }
          })
          return
        }
        setState(FRESH_EDITOR)
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
          edit((current) => ({
            value:
              current.value.slice(0, current.cursor - 1) +
              current.value.slice(current.cursor),
            cursor: current.cursor - 1,
            draft: current.draft,
            historyIndex: current.historyIndex,
            menuIndex: current.menuIndex,
            menuDismissed: current.menuDismissed,
          }))
        }
        return
      }
      // While the viewport owns j/k/g/G (scrolled-up vim keys), do not
      // insert them — the shell's useInput scrolls instead.
      if (
        !historyRecallEnabled &&
        (input === "j" ||
          input === "k" ||
          input === "g" ||
          input === "G")
      ) {
        return
      }
      if (input.length > 0) {
        edit((current) => ({
          value:
            current.value.slice(0, current.cursor) +
            input +
            current.value.slice(current.cursor),
          cursor: current.cursor + input.length,
          draft: current.draft,
          historyIndex: current.historyIndex,
          menuIndex: current.menuIndex,
          menuDismissed: current.menuDismissed,
        }))
      }
    },
    { isActive: active }
  )

  // -- reporting hooks (shell side: hotkey gates + viewport math) --------

  const inputRows = Math.max(1, state.value.split("\n").length)
  const renderedRows = (menuOpen ? matches.length : 0) + inputRows

  useEffect(() => {
    onMenuOpenChange?.(menuOpen)
  }, [menuOpen, onMenuOpenChange])

  useEffect(() => {
    onRowsChange?.(renderedRows)
  }, [renderedRows, onRowsChange])

  // -- render -------------------------------------------------------------

  const menuRows = matches.map((command, index) => {
    const selected = menuOpen && index === state.menuIndex
    return (
      <Text key={command.name}>
        {gutter}
        <Text
          color={selected ? palette.brand : undefined}
          dimColor={!selected}
        >
          {`/${command.name}`}
        </Text>
        <Text dimColor>{` — ${command.description}`}</Text>
      </Text>
    )
  })

  const lines = promptLines(state.value, state.cursor, placeholder)

  return (
    <Box flexDirection="column">
      {menuOpen ? menuRows : null}
      {lines.map((line, index) => (
        <Text key={index}>
          {index === 0 ? (
            <Text color={palette.brand}>
              {label}
              {symbols.prompt}{" "}
            </Text>
          ) : (
            // Continuation rows align under the prompt glyph.
            <Text>{"  "}</Text>
          )}
          {line}
        </Text>
      ))}
    </Box>
  )
}

/**
 * The value split into rendered lines with the cursor block injected
 * at the caret position: an inverse char over the character under the
 * caret, an inverse space at end-of-line positions (end of the buffer
 * or sitting on a newline). Pure — same inputs, same nodes.
 */
function promptLines(
  value: string,
  cursor: number,
  placeholder: string
): readonly React.ReactNode[][] {
  if (value.length === 0) {
    return [
      [
        <Text inverse key="cursor">
          {placeholder.slice(0, 1)}
        </Text>,
        <Text dimColor key="rest">
          {placeholder.slice(1)}
        </Text>,
      ],
    ]
  }
  const before = value.slice(0, cursor)
  const at = value[cursor]
  const after = at === undefined ? "" : value.slice(cursor + 1)

  const beforeLines = before.split("\n")
  const afterLines = after.split("\n")
  const head = beforeLines.slice(0, -1).map((line) => [
    <Text key="line">{line}</Text>,
  ])
  const lastBefore = beforeLines[beforeLines.length - 1] ?? ""

  if (at === undefined) {
    return [
      ...head,
      [
        <Text key="before">{lastBefore}</Text>,
        <Text inverse key="cursor">
          {" "}
        </Text>,
      ],
    ]
  }
  if (at === "\n") {
    // Caret on a newline: an inverse space ends the current line; the
    // newline itself carries the rest onto the next rendered row.
    return [
      ...head,
      [
        <Text key="before">{lastBefore}</Text>,
        <Text inverse key="cursor">
          {" "}
        </Text>,
      ],
      ...afterLines.map((line) => [<Text key="line">{line}</Text>]),
    ]
  }
  return [
    ...head,
    [
      <Text key="before">{lastBefore}</Text>,
      <Text inverse key="cursor">
        {at}
      </Text>,
      ...(afterLines[0] !== undefined
        ? [<Text key="after">{afterLines[0]}</Text>]
        : []),
    ],
    ...afterLines.slice(1).map((line) => [<Text key="line">{line}</Text>]),
  ]
}
