/**
 * `⠋ comuki thinking` — braille spinner in the accent color, with the
 * dimmed tail of whatever the brain is streaming right now.
 */
import { Text } from "ink"
import React, { useEffect, useState } from "react"
import { colors, symbols } from "../theme"
import { truncateTail } from "../lib/format"

export interface TypingIndicatorProps {
  readonly label?: string
  /** Dimmed last line(s) of the live chunk stream. */
  readonly liveText?: string
}

export function TypingIndicator({
  label = "comuki thinking",
  liveText,
}: TypingIndicatorProps) {
  const [frame, setFrame] = useState(0)

  useEffect(() => {
    const timer = setInterval(() => {
      setFrame((current) => (current + 1) % symbols.spinnerFrames.length)
    }, 90)
    return () => clearInterval(timer)
  }, [])

  const spinner = symbols.spinnerFrames[frame] ?? "⠋"
  const tail =
    liveText !== undefined && liveText.trim().length > 0
      ? `  ${truncateTail(liveText.replace(/\n/g, " ").trimEnd(), 60)}`
      : ""

  return (
    <Text>
      {"     "}
      <Text color="#8787f3">{spinner}</Text> {label}
      <Text dimColor>{tail}</Text>
    </Text>
  )
}
