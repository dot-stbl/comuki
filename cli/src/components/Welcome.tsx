/**
 * The centered first-run state: a big bordered wordmark with a tagline
 * and best-effort platform stats. The outer Box centers the card
 * horizontally and lets the parent flexbox push it to vertical middle;
 * padding stays modest so the card doesn't dominate small terminals.
 *
 * Rendered only while zero sessions exist and the first message has not
 * been sent — after that the chat owns the screen and the wordmark never
 * returns (until restart).
 */
import { Box, Text } from "ink"
import React from "react"

export interface PlatformStats {
  readonly workers: number
  readonly memory: number
}

export interface WelcomeProps {
  readonly stats?: PlatformStats | null
}

const ACCENT = "#8787f3"

export function Welcome({ stats }: WelcomeProps) {
  return (
    <Box
      flexDirection="column"
      alignItems="center"
      borderStyle="round"
      borderColor={ACCENT}
      paddingX={4}
      paddingY={1}
    >
      <Text bold color={ACCENT}>
        comuki
      </Text>
      <Box marginTop={1}>
        <Text dimColor>agent orchestration platform</Text>
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
