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

/** Async clipboard write — injectable so tests never touch the OS. */
export type ClipboardWriter = (text: string) => Promise<void>

export interface UseCopyLastAnswerOptions {
  /** Override the writer (tests); defaults to `clipboardy.write`. */
  readonly write?: ClipboardWriter
  /** How long the hint stays on screen before fading (default 2s). */
  readonly hintMs?: number
}

export interface CopyLastAnswer {
  /** Transient confirmation text, `null` when there is nothing to show. */
  readonly hint: string | null
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
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    getterRef.current = getLastAnswer
  }, [getLastAnswer])

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

  useInput((input, key) => {
    if (!key.ctrl || input !== "y") {
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

  return { hint }
}
