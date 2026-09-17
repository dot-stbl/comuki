/**
 * The centered first-run state: wordmark, tagline, best-effort platform
 * stats. Rendered only while zero sessions exist and the first message
 * has not been sent — after that the chat owns the screen and the
 * wordmark never returns (until restart).
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

export function Welcome({ stats }: WelcomeProps) {
  return (
    <Box flexDirection="column" alignItems="center" paddingY={1}>
      <Text bold color="#8787f3">
        comuki
      </Text>
      <Text dimColor>agent orchestration platform</Text>
      {stats ? (
        <Text dimColor>
          workers {stats.workers} · memory {stats.memory}
        </Text>
      ) : null}
      <Box paddingY={1} flexDirection="column" alignItems="center">
        <Text dimColor>enter send · / commands</Text>
      </Box>
    </Box>
  )
}
