/**
 * The centered first-run lockup: the brand glyph over the `comuki`
 * wordmark in the brand color, a dim tagline beneath, then a quiet
 * key-hints row and best-effort platform stats. No box — the terminal
 * is the chrome; the lockup floats in the vertical middle of the
 * screen while the parent flexbox holds it there.
 *
 * Rendered only while zero sessions exist and the first message has not
 * been sent — after that the chat owns the screen and the wordmark never
 * returns (until restart).
 */
import { Box, Text } from "ink"
import React from "react"
import { palette, symbols } from "../theme"

export interface PlatformStats {
  readonly workers: number
  readonly memory: number
}

export interface WelcomeProps {
  readonly stats?: PlatformStats | null
}

const HINTS = ["ctrl+n new tab", "esc sessions", "help commands"].join(
  ` ${symbols.bullet} `
)

export function Welcome({ stats }: WelcomeProps) {
  return (
    <Box flexDirection="column" alignItems="center" paddingX={2}>
      <Text color={palette.brand}>{symbols.brandMark}</Text>
      <Text bold color={palette.brand}>
        comuki
      </Text>
      <Box marginTop={1}>
        <Text dimColor>agent orchestration platform</Text>
      </Box>
      <Box marginTop={1}>
        <Text dimColor>{HINTS}</Text>
      </Box>
      {stats ? (
        <Box marginTop={1}>
          <Text dimColor>
            workers {stats.workers} · memory {stats.memory}
          </Text>
        </Box>
      ) : null}
    </Box>
  )
}
