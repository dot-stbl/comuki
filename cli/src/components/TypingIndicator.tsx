/**
 * `⠋ comuki is thinking…` — the braille spinner in the brand color on
 * the transcript gutter, with the dimmed tail of whatever the brain is
 * streaming right now. Frames advance through `nextSpinnerFrame`
 * (~80ms per step) on an injectable clock (see `hooks/useBlink` —
 * `bun:test` has no fake timers), cleared on unmount.
 */
import { Text } from "ink"
import React, { useEffect, useState } from "react"
import {
  gutter,
  nextSpinnerFrame,
  palette,
  symbols,
} from "../theme"
import { truncateTail } from "../lib/format"
import {
  systemIntervalClock,
  type IntervalClock,
} from "../hooks/useBlink"

export const SPINNER_INTERVAL_MS = 80

export interface TypingIndicatorProps {
  readonly label?: string
  /** Dimmed last line(s) of the live chunk stream. */
  readonly liveText?: string
  /** Frame cadence; overridable for slow terminals. */
  readonly frameIntervalMs?: number
  /** Injectable clock — tests drive frames deterministically. */
  readonly clock?: IntervalClock
}

export function TypingIndicator({
  label = "comuki is thinking…",
  liveText,
  frameIntervalMs = SPINNER_INTERVAL_MS,
  clock = systemIntervalClock,
}: TypingIndicatorProps) {
  const [frame, setFrame] = useState(0)

  useEffect(() => {
    const timer = clock.setInterval(() => {
      setFrame((current) => nextSpinnerFrame(current))
    }, frameIntervalMs)
    return () => clock.clearInterval(timer)
  }, [clock, frameIntervalMs])

  const spinner = symbols.spinnerFrames[frame] ?? "⠋"
  const tail =
    liveText !== undefined && liveText.trim().length > 0
      ? `  ${truncateTail(liveText.replace(/\n/g, " ").trimEnd(), 60)}`
      : ""

  return (
    <Text>
      {gutter}
      <Text color={palette.brand}>{spinner}</Text> <Text dimColor>{label}</Text>
      <Text dimColor>{tail}</Text>
    </Text>
  )
}
