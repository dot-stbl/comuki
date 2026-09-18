/**
 * The centered first-run lockup: the brand glyph over the `comuki`
 * wordmark in the brand color and three useful starting actions. The
 * parent centers it directly on the terminal floor without a card.
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
import { MARK_WELCOME } from "../lib/mark"
import { palette } from "../theme"

export interface WelcomeProps {
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

export function Welcome({ firstRun = firstRunHintVisible() }: WelcomeProps) {
  return (
    <Box flexDirection="column" alignItems="center">
      {MARK_WELCOME.map((line, index) => (
        <Text key={index} color={palette.brand}>
          {line}
        </Text>
      ))}
      <Box marginTop={1}>
        <Text bold color={palette.brand}>
          comuki agent harness
        </Text>
      </Box>
      <Box marginTop={1}>
        <Text dimColor>
          Describe the outcome you want. Comuki coordinates the work.
        </Text>
      </Box>
      <Box marginTop={1} flexDirection="column">
        <Text dimColor>{"  > ask a question or describe a task"}</Text>
        <Text dimColor>{"  / open an action by name"}</Text>
        <Text dimColor>{"  ctrl+p browse actions / esc browse sessions"}</Text>
      </Box>
      {firstRun ? (
        <Box marginTop={1}>
          <Text dimColor>first run? try: comuki setup</Text>
        </Box>
      ) : null}
    </Box>
  )
}
