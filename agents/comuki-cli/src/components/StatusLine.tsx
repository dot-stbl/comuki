/**
 * The one-shot header line: `comuki v0.1 · <identity> · project: nova`.
 * Printed at the top of every command's output; pure text, accent
 * separators, no chrome.
 */
import { Text } from "ink"
import React from "react"

export interface StatusLineProps {
  readonly identity: string
  readonly project?: string
  readonly extra?: string
}

export const CLI_VERSION = "0.1.0"

export function StatusLine({ identity, project, extra }: StatusLineProps) {
  const parts = [`comuki v${CLI_VERSION}`, identity]
  if (project) {
    parts.push(`project: ${project}`)
  }
  if (extra) {
    parts.push(extra)
  }
  return (
    <Text dimColor>
      {"  "}
      {parts.map((part, index) => (
        <React.Fragment key={part}>
          {index > 0 ? (
            <Text color="#8787f3"> · </Text>
          ) : null}
          <Text>{part}</Text>
        </React.Fragment>
      ))}
    </Text>
  )
}
