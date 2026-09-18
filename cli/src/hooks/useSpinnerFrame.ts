/**
 * The spinner frame driver for the in-flight typing row: cycles
 * 0..frames-1 every 90ms while `active`, freezes at 0 otherwise.
 * The row itself is formatted by `typingLines` in `lib/transcript.ts`
 * so the viewport renders it as data.
 */
import { useEffect, useState } from "react"
import { symbols } from "../theme"

export const SPINNER_INTERVAL_MS = 90

export function useSpinnerFrame(active: boolean): number {
  const [frame, setFrame] = useState(0)

  useEffect(() => {
    if (!active) {
      setFrame(0)
      return
    }
    const timer = setInterval(() => {
      setFrame((current) => (current + 1) % symbols.spinnerFrames.length)
    }, SPINNER_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [active])

  return frame
}
