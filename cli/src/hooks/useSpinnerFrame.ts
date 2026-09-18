/**
 * The spinner frame driver for the in-flight typing row: cycles
 * 0..frames-1 every 90ms while `active`, freezes at 0 otherwise.
 * The row itself is formatted by `typingLine` in `lib/transcript.ts`
 * so the viewport renders it as data.
 */
import { useEffect, useState } from "react"
import { MARK_TINY_FRAMES } from "../lib/mark"

export const SPINNER_INTERVAL_MS = 90

export function useSpinnerFrame(active: boolean): number {
  const [frame, setFrame] = useState(0)

  useEffect(() => {
    if (!active) {
      setFrame(0)
      return
    }
    const timer = setInterval(() => {
      setFrame((current) => (current + 1) % MARK_TINY_FRAMES.length)
    }, SPINNER_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [active])

  return frame
}
