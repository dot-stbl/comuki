/**
 * A filled rectangle behind its children. Ink 5 paints `backgroundColor`
 * on `<Text>` only (Box support landed in 6.1), so a band is a stack of
 * space-rows with the hex, absolutely positioned under the content.
 */
import { Box, Text } from "ink"
import React from "react"

export interface FillProps {
  readonly width: number
  readonly height: number
  readonly color: string
  readonly children?: React.ReactNode
}

/** Exact visible rows painted behind children; exported for width invariants. */
export function fillRows(width: number, height: number): readonly string[] {
  const columns = Math.max(0, width)
  const rows = Math.max(0, height)
  return Array.from({ length: rows }, () => " ".repeat(columns))
}

export function Fill({ width, height, color, children }: FillProps) {
  const columns = Math.max(0, width)
  const backgroundRows = fillRows(columns, height)
  return (
    <Box width={columns} height={backgroundRows.length} flexDirection="column">
      <Box position="absolute" flexDirection="column">
        {backgroundRows.map((row, index) => (
          <Text key={index} backgroundColor={color}>
            {row}
          </Text>
        ))}
      </Box>
      {children}
    </Box>
  )
}
