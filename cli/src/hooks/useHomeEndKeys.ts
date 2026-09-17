/**
 * Home / End key detection.
 *
 * Ink 5's `useInput` key object stops at arrows / page keys — Home and
 * End are parsed (`parseKeypress` knows their names) but never exposed
 * on the key flags, and their `input` is blanked for non-alphanumeric
 * keys. So this hook subscribes to the same internal `input` emitter
 * useInput listens on and matches the raw escape sequences itself.
 *
 * PgUp/PgDn, by contrast, ARE exposed (`key.pageUp` / `key.pageDown`)
 * and stay in `useInput`; only Home/End need this side channel.
 */
import { useEffect, useRef } from "react"
import { useStdin } from "ink"

export type HomeEndKey = "home" | "end"

/** xterm `[H`/`[F`, VT/rxvt `[1~`/`[4~`/`[7~`/`[8~`, SS3 `OH`/`OF`. */
const HOME_SEQUENCES: readonly string[] = [
  "\x1b[H",
  "\x1b[1~",
  "\x1b[7~",
  "\x1bOH",
]
const END_SEQUENCES: readonly string[] = [
  "\x1b[F",
  "\x1b[4~",
  "\x1b[8~",
  "\x1bOF",
]

/** Pure matcher over one stdin chunk — exported for tests. */
export function matchHomeEnd(chunk: string): HomeEndKey | null {
  if (HOME_SEQUENCES.includes(chunk)) {
    return "home"
  }
  if (END_SEQUENCES.includes(chunk)) {
    return "end"
  }
  return null
}

export function useHomeEndKeys(
  onHome: () => void,
  onEnd: () => void,
  active: boolean = true
): void {
  const { internal_eventEmitter } = useStdin()
  const handlers = useRef({ onHome, onEnd })
  useEffect(() => {
    handlers.current = { onHome, onEnd }
  }, [onHome, onEnd])

  useEffect(() => {
    if (!active || !internal_eventEmitter) {
      return
    }
    const onInput = (chunk: unknown) => {
      const data = typeof chunk === "string" ? chunk : String(chunk)
      const hit = matchHomeEnd(data)
      if (hit === "home") {
        handlers.current.onHome()
      } else if (hit === "end") {
        handlers.current.onEnd()
      }
    }
    internal_eventEmitter.on("input", onInput)
    return () => {
      internal_eventEmitter.off("input", onInput)
    }
  }, [active, internal_eventEmitter])
}
