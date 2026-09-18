import { Box, Text } from "ink"
import React from "react"
import { stripAnsi } from "../theme"

export interface ContextWorkbenchProps {
  readonly title: string
  readonly lines: readonly string[]
  readonly width: number
  readonly height: number
}

export function ContextWorkbench({
  title,
  lines,
  width,
  height,
}: ContextWorkbenchProps) {
  const bodyRows = Math.max(1, height - 2)
  return (
    <Box flexDirection="column" width={width} height={height} paddingX={1}>
      <Text bold>{title}</Text>
      {lines.slice(0, bodyRows).map((line, index) => (
        <Text key={index}>{fitLine(line, Math.max(1, width - 2))}</Text>
      ))}
    </Box>
  )
}

function fitLine(line: string, width: number): string {
  const plain = stripAnsi(line)
  return plain.length <= width ? line : `${plain.slice(0, Math.max(1, width - 3))}...`
}
