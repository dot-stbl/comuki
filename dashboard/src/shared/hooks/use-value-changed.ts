import { useEffect, useRef, useState } from "react"

/**
 * True for a moment after `value` changes.
 *
 * The duty screen is returned to every few minutes, not watched: without this,
 * a number that moved while nobody was looking is indistinguishable from one
 * that never moved. Never fires on first render, so arriving at the screen does
 * not light it up.
 */
export function useValueChanged(value: number, holdMs = 900): boolean {
  const previous = useRef(value)
  const [changed, setChanged] = useState(false)

  useEffect(() => {
    if (previous.current === value) {
      return
    }
    previous.current = value
    setChanged(true)
    const timer = window.setTimeout(() => setChanged(false), holdMs)
    return () => window.clearTimeout(timer)
  }, [value, holdMs])

  return changed
}
