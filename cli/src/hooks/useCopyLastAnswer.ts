/**
 * ctrl+y copies the last assistant answer to the system clipboard.
 *
 * The key handler lives here (its own `useInput`) so the shell does not
 * grow another hotkey branch — Ink fans every key out to all active
 * listeners and `PromptInput` ignores ctrl-combos, so the hook cannot
 * clash with the editor. The clipboard writer is injectable; the
 * default is `clipboardy`, which shells out to clip/pbcopy/xclip.
 *
 * Confirmation is a transient hint (returned as a string, rendered by
 * the caller near the prompt) — never a modal, never input blocking.
 */
import { useInput } from "ink"
import { useCallback, useEffect, useRef, useState } from "react"
import clipboardy from "clipboardy"
import { matchesBinding } from "../lib/keybindings"

/** Async clipboard write — injectable so tests never touch the OS. */
export type ClipboardWriter = (text: string) => Promise<void>

export interface UseCopyLastAnswerOptions {
  /** Override the writer (tests); defaults to `clipboardy.write`. */
  readonly write?: ClipboardWriter
  /** How long the hint stays on screen before fading (default 2s). */
  readonly hintMs?: number
  /**
   * Last fenced code block — used by ctrl+shift+y / `/copycode`.
   * `null` / empty → "no code block".
   */
  readonly getLastCodeFence?: () => string | null
  /** Chord from the keybindings overlay; defaults to `ctrl+y`. */
  readonly chord?: string
}

export interface CopyLastAnswer {
  /** Transient confirmation text, `null` when there is nothing to show. */
  readonly hint: string | null
  /** `/copycode` — same write + hint path as ctrl+shift+y. */
  readonly copyLastCode: () => void
}

export function useCopyLastAnswer(
  getLastAnswer: () => string | undefined,
  options: UseCopyLastAnswerOptions = {}
): CopyLastAnswer {
  const write = options.write ?? ((text: string) => clipboardy.write(text))
  const hintMs = options.hintMs ?? 2000
  const [hint, setHint] = useState<string | null>(null)
  // Fresh-on-every-render getter without re-registering the key handler.
  const getterRef = useRef(getLastAnswer)
  const codeGetterRef = useRef(options.getLastCodeFence)
  const chordRef = useRef(options.chord ?? "ctrl+y")
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    getterRef.current = getLastAnswer
  }, [getLastAnswer])

  useEffect(() => {
    codeGetterRef.current = options.getLastCodeFence
  }, [options.getLastCodeFence])

  useEffect(() => {
    chordRef.current = options.chord ?? "ctrl+y"
  }, [options.chord])

  useEffect(
    () => () => {
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current)
      }
    },
    []
  )

  const flash = useCallback(
    (text: string) => {
      setHint(text)
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current)
      }
      timerRef.current = setTimeout(() => setHint(null), hintMs)
    },
    [hintMs]
  )

  const copyCode = useCallback(() => {
    const text = codeGetterRef.current?.() ?? null
    if (text === null || text.length === 0) {
      flash("no code block")
      return
    }
    flash("copied code ✓")
    void write(text).catch(() => setHint("copy failed"))
  }, [flash, write])

  useInput((input, key) => {
    if (key.ctrl && (key.shift || input === "Y") && (input === "y" || input === "Y")) {
      copyCode()
      return
    }
    if (!matchesBinding(chordRef.current, input, key)) {
      return
    }
    const text = getterRef.current()
    if (text === undefined || text.length === 0) {
      flash("nothing to copy")
      return
    }
    flash("copied ✓")
    // Best effort: a failed write downgrades the hint instead of throwing
    // inside the key handler.
    void write(text).catch(() => setHint("copy failed"))
  })

  return { hint, copyLastCode: copyCode }
}
