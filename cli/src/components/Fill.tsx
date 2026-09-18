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

export function Fill({ width, height, color, children }: FillProps) {
  const columns = Math.max(0, width)
  const rows = Math.max(0, height)
  return (
    <Box width={columns} height={rows} flexDirection="column">
      <Box position="absolute" flexDirection="column">
        {Array.from({ length: rows }, (_, index) => (
          <Text key={index} backgroundColor={color}>
            {" ".repeat(columns)}
          </Text>
        ))}
      </Box>
      {children}
    </Box>
  )
}
