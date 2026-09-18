/**
 * The centered first-run lockup: the brand glyph over the `comuki`
 * wordmark in the brand color, a dim tagline beneath, then a quiet
 * key-hints row and best-effort platform stats. No box — the terminal
 * is the chrome; the lockup floats in the vertical middle of the
 * screen while the parent flexbox holds it there.
 *
 * Rendered only while zero sessions exist and the first message has not
 * been sent — after that the chat owns the screen and the wordmark never
 * returns (until restart). While `~/.config/comuki/config.json` does
 * not exist, one extra dim line points at `comuki setup`; the guard is
 * the file itself, so the hint can never survive a completed setup.
 */
import { existsSync } from "node:fs"
import { Box, Text } from "ink"
import React from "react"
import { configFilePath } from "../lib/config"
import { MARK_SMALL } from "../lib/mark"
import { palette, symbols } from "../theme"

export interface PlatformStats {
  readonly workers: number
  readonly memory: number
}

export interface WelcomeProps {
  readonly stats?: PlatformStats | null
  /**
   * Show the `comuki setup` pointer. Defaults to "config.json missing"
   * so chat.tsx needs no wiring; pass explicitly in tests.
   */
  readonly firstRun?: boolean
}

/** True while the config file does not exist — the REPL's first-run signal. */
export function firstRunHintVisible(
  configPath: string = configFilePath()
): boolean {
  return !existsSync(configPath)
}

const HINTS = ["ctrl+n new tab", "esc sessions", "help commands"].join(
  ` ${symbols.bullet} `
)

export function Welcome({ stats, firstRun = firstRunHintVisible() }: WelcomeProps) {
  return (
    <Box flexDirection="column" alignItems="center" paddingX={2} paddingY={1}>
      {MARK_SMALL.map((line, index) => (
        <Text key={index} color={palette.brand} backgroundColor={palette.lane}>
          {line}
        </Text>
      ))}
      <Box marginTop={1}>
        <Text bold color={palette.brand} backgroundColor={palette.lane}>
          comuki
        </Text>
      </Box>
      <Box marginTop={1}>
        <Text dimColor backgroundColor={palette.lane}>
          agent orchestration platform
        </Text>
      </Box>
      <Box marginTop={1}>
        <Text dimColor backgroundColor={palette.lane}>
          {HINTS}
        </Text>
      </Box>
      {stats ? (
        <Box marginTop={1}>
          <Text dimColor backgroundColor={palette.lane}>
            workers {stats.workers} · memory {stats.memory}
          </Text>
        </Box>
      ) : null}
      {firstRun ? (
        <Box marginTop={1}>
          <Text dimColor backgroundColor={palette.lane}>
            first run? try: comuki setup
          </Text>
        </Box>
      ) : null}
    </Box>
  )
}
