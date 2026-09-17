import { useEffect, useState } from "react"

/**
 * A value, once it has stopped changing for `ms`.
 *
 * Written inside `knowledge/api/queries.ts` as a file-local helper when the
 * search box started firing a cosine query per keystroke and the list under it
 * blanked to "No matches" between one answer and the next. The project had no
 * shared debounce at all, so the next search box would have written a third
 * copy — it is here before that happens.
 *
 * It settles rather than throttles: the timer restarts on every change, so a
 * burst of keystrokes produces exactly one settled value, and an unmounted
 * component leaves no pending write behind.
 */
export function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => {
      setSettled(value)
    }, ms)
    return () => {
      clearTimeout(timer)
    }
  }, [value, ms])

  return settled
}
