/**
 * Dimmed thinking + tool-step block. Used for replayed history (parts of
 * an assistant message) and for progressive steps collected off the live
 * chunk stream — either way the lines arrive pre-classified:
 * `thinking` renders dimmed gray, `tool` muted mono.
 */
import { Text } from "ink"
import React from "react"

export interface ThinkingBlockProps {
  readonly thinking: readonly string[]
  readonly toolSteps: readonly string[]
}

export function ThinkingBlock({ thinking, toolSteps }: ThinkingBlockProps) {
  return (
    <>
      {thinking.map((line, index) => (
        <Text key={`think-${index}`} dimColor>
          {`  ${line}`}
        </Text>
      ))}
      {toolSteps.map((step, index) => (
        <Text key={`tool-${index}`} color="gray">
          {`  ${step}`}
        </Text>
      ))}
    </>
  )
}
